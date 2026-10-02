using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class WaterImportService(ApplicationDbContext db, TimeProvider clock)
{
    public const int MaxExisting = 50000;
    public const int MaxComparisons = 1000000;
    public int LastComparisonCount { get; private set; }

    public async Task<IReadOnlyList<WaterImportRow>> ClassifyAsync(IReadOnlyList<WaterImportRow> rows, CancellationToken ct = default, int? excludeId = null)
    {
        LastComparisonCount = 0;
        if (rows.Count > BinImportCsv.MaxRows) throw new BinImportException("Preveč vrstic.");
        var valid = rows.Where(r => r.Status != ImportRowStatus.Invalid).ToArray();
        if (valid.Any(r => WaterImportMappingRules.Errors(r).Any())) throw new BinImportException("Neveljavni podatki predogleda. Ponovi uvoz.");
        if (valid.Length == 0) return rows;
        var delta = WaterPoints.DuplicateMetres / 110000d;
        var south = Math.Max(-90, valid.Min(r => r.Latitude!.Value) - delta);
        var north = Math.Min(90, valid.Max(r => r.Latitude!.Value) + delta);
        var lonDelta = Math.Min(180, delta / Math.Max(.000001, Math.Cos(Math.Max(Math.Abs(south), Math.Abs(north)) * Math.PI / 180)));
        var west = valid.Min(r => r.Longitude!.Value) - lonDelta;
        var east = valid.Max(r => r.Longitude!.Value) + lonDelta;
        var allLongitudes = west < -180 || east > 180;
        // Include active, pending and retired WaterPoints, regardless of name or source.
        // Never truncate detection: oversized areas fail closed.
        var existing = await db.WaterPoints.AsNoTracking().Where(p => p.Id != excludeId && p.Latitude >= south && p.Latitude <= north &&
            (allLongitudes || p.Longitude >= west && p.Longitude <= east)).OrderBy(p => p.Id).Take(MaxExisting + 1)
            .Select(p => new { p.Id, p.Name, p.Latitude, p.Longitude }).ToListAsync(ct);
        if (existing.Count > MaxExisting) throw new BinImportException("Območje vsebuje več kot 50.000 lokacij. Razdeli CSV na manjša območja.");
        var grid = new Dictionary<(int, int, int), List<(double Lat, double Lon, WaterImportCandidate Candidate)>>();
        void Add(double lat, double lon, WaterImportCandidate candidate)
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180) return;

            var (x, y, z) = Cell(lat, lon);
            var key = (x, y, z);
            if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = [];
            bucket.Add((lat, lon, candidate));
        }
        foreach (var point in existing) Add(point.Latitude, point.Longitude, new(point.Id, null, point.Name ?? "Pitnik", 0));
        var result = new List<WaterImportRow>(rows.Count);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (row.Status == ImportRowStatus.Invalid) { result.Add(row); continue; }

            var lat = row.Latitude!.Value; var lon = row.Longitude!.Value; var cell = Cell(lat, lon);
            var candidates = new List<WaterImportCandidate>();
            for (var x = -1; x <= 1 && candidates.Count < 3; x++)
                for (var y = -1; y <= 1 && candidates.Count < 3; y++)
                    for (var z = -1; z <= 1 && candidates.Count < 3; z++)
                    {
                        if (!grid.TryGetValue((cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var bucket)) continue;
                        foreach (var other in bucket)
                        {
                            if (++LastComparisonCount > MaxComparisons) throw new BinImportException("Preveč primerjav dvojnikov. Razdeli CSV na manjša območja.");
                            var distance = DuplicateCandidates.Distance(lat, lon, other.Lat, other.Lon);
                            if (distance <= WaterPoints.DuplicateMetres) candidates.Add(other.Candidate with { Metres = distance });
                            if (candidates.Count == 3) break;
                        }
                    }
            result.Add(row with { Candidates = candidates, Status = candidates.Count == 0 ? ImportRowStatus.Ready : ImportRowStatus.PossibleDuplicate });
            // Conservative chain detection includes earlier valid rows even if excluded as duplicates.
            Add(lat, lon, new(null, row.Number, row.Name, 0));
        }
        return result;
    }
    private static (int, int, int) Cell(double lat, double lon)
    {
        lat *= Math.PI / 180; lon *= Math.PI / 180;
        const double radius = 6371000;
        return ((int)Math.Floor(radius * Math.Cos(lat) * Math.Cos(lon) / WaterPoints.DuplicateMetres),
            (int)Math.Floor(radius * Math.Cos(lat) * Math.Sin(lon) / WaterPoints.DuplicateMetres),
            (int)Math.Floor(radius * Math.Sin(lat) / WaterPoints.DuplicateMetres));
    }
    public async Task<int> ImportAsync(int sourceId, IReadOnlyList<WaterImportRow> selected, CancellationToken ct = default)
    {
        if (selected.Count is < 1 or > BinImportCsv.MaxRows || selected.Any(r => r.Status != ImportRowStatus.Ready) ||
            selected.Select(r => r.Number).Distinct().Count() != selected.Count) throw new BinImportException("Izberi pripravljene vrstice.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (db.Database.IsNpgsql())
                await WaterPoints.LockAsync(db, ct);
            if (!await db.DataSources.AnyAsync(s => s.Id == sourceId, ct)) throw new BinImportException("Izbrani vir ne obstaja več. Ponovi uvoz.");
            var current = await ClassifyAsync(selected, ct);
            if (current.Any(r => r.Status != ImportRowStatus.Ready)) throw new BinImportException("Pojavili so se novi možni dvojniki. Nobena vrstica ni uvožena; ponovno preveri predogled.");
            // PostgreSQL microsecond precision for the concurrency token.
            var now = new DateTime(clock.GetUtcNow().UtcDateTime.Ticks / 10 * 10, DateTimeKind.Utc);
            db.WaterPoints.AddRange(current.Select(row =>
            {
                var point = new WaterPoint { DateAdded = now, ApprovedAt = now, UpdatedAt = now };
                row.Input().ApplyTo(point);
                point.DataSourceId = sourceId;
                return point;
            }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return current.Count;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
