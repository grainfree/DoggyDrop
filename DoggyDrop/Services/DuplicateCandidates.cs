using System.Text.RegularExpressions;
using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed record DuplicateRecord(int Id, string Name, double Latitude, double Longitude, string? Source, bool Active);
public sealed record DuplicatePair(DuplicateRecord First, DuplicateRecord Second, double Metres);
public sealed record DuplicateResult(IReadOnlyList<DuplicatePair> Pairs, int Comparisons, bool Limited, bool TooManyRecords = false);
public sealed record DuplicatePage(BulkTarget Target, double? Latitude, double? Longitude, int Radius, DuplicateResult? Result);

public sealed class DuplicateCandidates(ApplicationDbContext db)
{
    public const int MaxRecords = 2000;
    public const int MaxPairs = 200;
    public const int MaxComparisons = 50000;
    public const double BinMetres = 20;
    public const double PlaceMetres = 75;
    private const double EarthRadius = 6371000;

    public async Task<DuplicateResult> FindAsync(BulkTarget target, double latitude, double longitude, int radius)
    {
        if (!Enum.IsDefined(target) || !double.IsFinite(latitude) || !double.IsFinite(longitude) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180 || radius is < 100 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(radius));
        var latitudeDelta = radius / 110000d;
        var south = Math.Max(-90, latitude - latitudeDelta); var north = Math.Min(90, latitude + latitudeDelta);
        var maxAbsLatitude = Math.Max(Math.Abs(south), Math.Abs(north));
        var longitudeDelta = Math.Min(180, radius / (110000 * Math.Max(0.000001, Math.Cos(maxAbsLatitude * Math.PI / 180))));
        var west = longitude - longitudeDelta; var east = longitude + longitudeDelta;
        var query = target == BulkTarget.Bins
            ? db.TrashBins.AsNoTracking().Select(b => new { b.Id, b.Name, b.Latitude, b.Longitude,
                Source = b.DataSource == null ? null : b.DataSource.Name, Active = b.IsApproved })
            : db.Places.AsNoTracking().Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude,
                Source = p.DataSource == null ? null : p.DataSource.Name, Active = p.IsActive });
        var rows = await query.Where(r => r.Latitude >= south && r.Latitude <= north &&
            (longitudeDelta >= 180 || (west < -180 ? r.Longitude >= west + 360 || r.Longitude <= east :
                east > 180 ? r.Longitude >= west || r.Longitude <= east - 360 : r.Longitude >= west && r.Longitude <= east)))
            .OrderBy(r => r.Id).Take(MaxRecords + 1)
            .Select(r => new DuplicateRecord(r.Id, r.Name, r.Latitude, r.Longitude, r.Source, r.Active)).ToListAsync();
        if (rows.Count > MaxRecords) return new([], 0, true, true); // Ask for a smaller area; never scan a hidden national set.
        return Scan(rows.Where(r => Distance(latitude, longitude, r.Latitude, r.Longitude) <= radius), target);
    }

    public static DuplicateResult Scan(IEnumerable<DuplicateRecord> records, BulkTarget target)
    {
        var threshold = target == BulkTarget.Bins ? BinMetres : PlaceMetres;
        var grid = new Dictionary<(string Name, int X, int Y, int Z), List<DuplicateRecord>>();
        var pairs = new List<DuplicatePair>(); var comparisons = 0;
        foreach (var row in records)
        {
            if (!double.IsFinite(row.Latitude) || !double.IsFinite(row.Longitude) || row.Latitude is < -90 or > 90 || row.Longitude is < -180 or > 180) continue;
            var name = target == BulkTarget.Places ? NormalizeName(row.Name) : "";
            if (target == BulkTarget.Places && name.Length == 0) continue;
            var lat = row.Latitude * Math.PI / 180; var lon = row.Longitude * Math.PI / 180;
            // 3D earth-centred buckets handle poles and the date line without special neighbour cases.
            // Any pair within the threshold differs by at most one bucket on each axis.
            var x = (int)Math.Floor(EarthRadius * Math.Cos(lat) * Math.Cos(lon) / threshold);
            var y = (int)Math.Floor(EarthRadius * Math.Cos(lat) * Math.Sin(lon) / threshold);
            var z = (int)Math.Floor(EarthRadius * Math.Sin(lat) / threshold);
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue((name, x + dx, y + dy, z + dz), out var neighbours)) continue;
                foreach (var other in neighbours)
                {
                    if (comparisons == MaxComparisons) return new(pairs, comparisons, true);
                    comparisons++;
                    var distance = Distance(row.Latitude, row.Longitude, other.Latitude, other.Longitude);
                    if (distance > threshold) continue;
                    pairs.Add(new(other, row, distance));
                    if (pairs.Count == MaxPairs) return new(pairs, comparisons, true);
                }
            }
            var key = (name, x, y, z);
            if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = [];
            bucket.Add(row);
        }
        return new(pairs, comparisons, false);
    }

    public static string NormalizeName(string value) => Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
    public static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        var radians = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * radians / 2), 2) +
            Math.Cos(lat1 * radians) * Math.Cos(lat2 * radians) * Math.Pow(Math.Sin((lon2 - lon1) * radians / 2), 2);
        return 2 * EarthRadius * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
