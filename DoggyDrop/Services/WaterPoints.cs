using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;
namespace DoggyDrop.Services;

public sealed record WaterPointMapItem(int Id, string Name, double Latitude, double Longitude,
    WaterAccess Access, WaterSeasonality Seasonality, WaterDogAccess DogAccess, string? SourceName, string? SourceUrl);

public static class WaterPoints
{
    // A conservative review guard, NOT a claim that close taps are the same object.
    public const double DuplicateMetres = 15;
    public static IQueryable<WaterPoint> PublicWater(this IQueryable<WaterPoint> points) => points.Where(p =>
        p.IsApproved && !p.IsRetired && p.Potability == WaterPotability.SourceReportedDrinking &&
        (p.Access == WaterAccess.Unknown || p.Access == WaterAccess.Public || p.Access == WaterAccess.Permissive) &&
        p.Latitude >= -90 && p.Latitude <= 90 && p.Longitude >= -180 && p.Longitude <= 180);
    public static async Task<List<WaterPointMapItem>> LoadAsync(IQueryable<WaterPoint> points, CancellationToken ct = default)
    {
        var rows = await points.AsNoTracking().PublicWater().OrderBy(p => p.Id).Select(p => new WaterPointMapItem(
            p.Id, p.Name ?? "Pitnik", p.Latitude, p.Longitude, p.Access, p.Seasonality, p.DogAccess,
            p.DataSource == null ? null : p.DataSource.Name, p.DataSource == null ? null : p.DataSource.WebsiteUrl)).ToListAsync(ct);
        return rows.Select(p => p with { Name = string.IsNullOrWhiteSpace(p.Name) ? "Pitnik" : p.Name,
            SourceUrl = SafeSourceUrl(p.SourceUrl) }).ToList();
    }
    public static string? SafeSourceUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
    public static Task LockAsync(ApplicationDbContext db, CancellationToken ct = default) => db.Database.IsNpgsql()
        ? db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(194721, 202)", ct) : Task.CompletedTask;
    public static string Status(WaterPoint p) => p.IsRetired ? "Umaknjen" : p.IsApproved ? "Aktiven" : "Čaka na odobritev";
}
