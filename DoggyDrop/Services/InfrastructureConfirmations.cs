using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed record ConfirmationLocation(double? Latitude, double? Longitude, double? Accuracy);
public sealed record ConfirmationResult(string Outcome, InfrastructureTrustSummary? Summary = null);

public static class ConfirmationRegistration
{
    public static IServiceCollection AddCommunityConfirmations(this IServiceCollection services)
    {
        services.AddRateLimiter(options => options.AddPolicy("infrastructure-confirmation", context =>
            System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }
}

public sealed class InfrastructureConfirmations(ApplicationDbContext db, TimeProvider clock)
{
    public const double RadiusMetres = 50;
    public const double MaximumAccuracyMetres = 25;
    public static readonly TimeSpan Cooldown = TimeSpan.FromHours(24);
    public static bool Valid(ConfirmationLocation? input) => input?.Latitude is double lat && double.IsFinite(lat) && lat is >= -90 and <= 90 &&
        input.Longitude is double lon && double.IsFinite(lon) && lon is >= -180 and <= 180 &&
        input.Accuracy is double accuracy && double.IsFinite(accuracy) && accuracy is >= 0 and <= 10000;
    public static string? Proximity(double distance, double accuracy) => accuracy > MaximumAccuracyMetres ? "accuracy" :
        distance > RadiusMetres ? "far" : distance + accuracy > RadiusMetres ? "accuracy" : null;

    public async Task<ConfirmationResult> ConfirmAsync(bool water, int id, string userId, ConfirmationLocation? input, CancellationToken ct = default)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(userId) || !Valid(input)) return new("invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Same locks as infrastructure changes/imports. Row locks also protect against writers
        // which do not take the advisory lock. Cooldown and insert share this transaction.
        if (water) await WaterPoints.LockAsync(db, ct); else await BinCommunityRules.LockAsync(db);
        Guid version; double lat, lon;
        if (water)
        {
            var query = db.Database.IsNpgsql()
                ? db.WaterPoints.FromSqlInterpolated($"SELECT * FROM \"WaterPoints\" WHERE \"Id\" = {id} FOR UPDATE")
                : db.WaterPoints.Where(p => p.Id == id);
            var p = await query.AsNoTracking().PublicWater().SingleOrDefaultAsync(ct);
            if (p == null) return new("unavailable");
            (version, lat, lon) = (p.EvidenceVersion, p.Latitude, p.Longitude);
        }
        else
        {
            var query = db.Database.IsNpgsql()
                ? db.TrashBins.FromSqlInterpolated($"SELECT * FROM \"TrashBins\" WHERE \"Id\" = {id} FOR UPDATE")
                : db.TrashBins.Where(p => p.Id == id);
            var b = await query.AsNoTracking().PublicBins().SingleOrDefaultAsync(ct);
            if (b == null) return new("unavailable");
            (version, lat, lon) = (b.EvidenceVersion, b.Latitude, b.Longitude);
        }
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180) return new("unavailable");
        var failure = Proximity(DuplicateCandidates.Distance(lat, lon, input!.Latitude!.Value, input.Longitude!.Value), input.Accuracy!.Value);
        if (failure != null) return new(failure);
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) return new("unavailable");
        var now = clock.GetUtcNow().UtcDateTime; var since = now - Cooldown;
        var previous = db.InfrastructureConfirmations.Where(c => c.UserId == userId && c.CreatedAt > since);
        var repeated = await (water ? previous.Where(c => c.WaterPointId == id) : previous.Where(c => c.TrashBinId == id)).AnyAsync(ct);
        if (!repeated)
        {
            db.InfrastructureConfirmations.Add(new() { TrashBinId = water ? null : id, WaterPointId = water ? id : null,
                Type = water ? InfrastructureConfirmationType.WaterPointWorking : InfrastructureConfirmationType.TrashBinPresent,
                UserId = userId, CreatedAt = now, EvidenceVersion = version });
            await db.SaveChangesAsync(ct);
        }
        var trust = new InfrastructureTrust(db, clock);
        var summaries = water ? await trust.WaterAsync([id], ct) : await trust.BinsAsync([id], ct);
        await transaction.CommitAsync(ct);
        return new(repeated ? "cooldown" : "accepted", summaries.GetValueOrDefault(id, InfrastructureTrust.Empty));
    }
}
