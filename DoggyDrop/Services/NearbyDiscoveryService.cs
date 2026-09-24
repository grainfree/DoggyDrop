using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class NearbyDiscoveryService
{
    private const int MaxRadiusMeters = 10_000;
    private const double EarthRadiusMeters = 6_371_000;
    private readonly ApplicationDbContext _context;

    public NearbyDiscoveryService(ApplicationDbContext context) => _context = context;

    public async Task NotifyForApprovedBinAsync(TrashBin bin)
    {
        if (!bin.IsApproved || bin.ApprovedAt is not { } approvedAt ||
            !ValidCoordinate(bin.Latitude, bin.Longitude)) return;

        var latitudeDelta = MaxRadiusMeters / 111_000d;
        var latitudeLimit = Math.Min(89.999, Math.Abs(bin.Latitude) + latitudeDelta);
        var longitudeDelta = Math.Min(180, latitudeDelta / Math.Max(0.00001, Math.Cos(latitudeLimit * Math.PI / 180)));
        var candidates = _context.NearbyDiscoveryPreferences.AsNoTracking()
            .Where(preference => preference.BinsEnabled && preference.EnabledAt <= approvedAt &&
                preference.Latitude >= bin.Latitude - latitudeDelta &&
                preference.Latitude <= bin.Latitude + latitudeDelta &&
                preference.UserId != bin.UserId);

        if (longitudeDelta < 180)
        {
            var minimum = bin.Longitude - longitudeDelta;
            var maximum = bin.Longitude + longitudeDelta;
            if (minimum < -180)
                candidates = candidates.Where(preference => preference.Longitude >= minimum + 360 || preference.Longitude <= maximum);
            else if (maximum > 180)
                candidates = candidates.Where(preference => preference.Longitude >= minimum || preference.Longitude <= maximum - 360);
            else
                candidates = candidates.Where(preference => preference.Longitude >= minimum && preference.Longitude <= maximum);
        }

        foreach (var preference in await candidates.ToListAsync())
        {
            if (!ValidCoordinate(preference.Latitude, preference.Longitude) ||
                preference.RadiusMeters is < 1 or > MaxRadiusMeters ||
                DistanceMeters(preference.Latitude, preference.Longitude, bin.Latitude, bin.Longitude) > preference.RadiusMeters + 0.001)
                continue;

            var sourceKey = $"Bin:{bin.Id}";
            var link = $"/Map?binId={bin.Id}";
            await _context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "UserNotifications" ("UserId", "Type", "Title", "Body", "LinkUrl", "SourceKey", "IsRead", "CreatedAt")
                VALUES ({{preference.UserId}}, 'NewBinNearby', 'Nov pasji koš v bližini',
                    'V tvojem izbranem območju je bil dodan nov pasji koš.', {{link}}, {{sourceKey}}, {{false}}, {{DateTime.UtcNow}})
                ON CONFLICT ("UserId", "SourceKey") DO NOTHING
                """);
        }
    }

    public static bool ValidCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    public static double DistanceMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        var latitudeDelta = (latitude2 - latitude1) * Math.PI / 180;
        var longitudeDelta = (longitude2 - longitude1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
            Math.Cos(latitude1 * Math.PI / 180) * Math.Cos(latitude2 * Math.PI / 180) *
            Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
