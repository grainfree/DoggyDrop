using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class BinImportService(ApplicationDbContext db, TimeProvider clock)
{
    public const int MaxExisting = 50000;
    public const int MaxComparisons = 1000000;
    private const double EarthRadius = 6371000;
    public int LastComparisonCount { get; private set; }

    public async Task<IReadOnlyList<ImportRow>> ClassifyAsync(IReadOnlyList<ImportRow> rows, CancellationToken ct = default)
    {
        LastComparisonCount = 0;
        if (rows.Count > BinImportCsv.MaxRows) throw new BinImportException("Preveč vrstic.");
        var valid = rows.Where(r => r.Status != ImportRowStatus.Invalid).ToArray();
        if (valid.Length == 0) return rows;
        if (valid.Any(r => r.Latitude == null || r.Longitude == null || !double.IsFinite(r.Latitude.Value) || !double.IsFinite(r.Longitude.Value) ||
            r.Latitude is < -90 or > 90 || r.Longitude is < -180 or > 180 || string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > BinImportMapping.MaxDisplayName))
            throw new BinImportException("Neveljavni podatki predogleda. Ponovi uvoz.");
        var delta = DuplicateCandidates.BinMetres / 110000d;
        var south = Math.Max(-90, valid.Min(r => r.Latitude!.Value) - delta);
        var north = Math.Min(90, valid.Max(r => r.Latitude!.Value) + delta);
        var lonDelta = Math.Min(180, delta / Math.Max(.000001, Math.Cos(Math.Max(Math.Abs(south), Math.Abs(north)) * Math.PI / 180)));
        var west = valid.Min(r => r.Longitude!.Value) - lonDelta;
        var east = valid.Max(r => r.Longitude!.Value) + lonDelta;
        // A wrapping box deliberately includes all longitudes; the row cap fails closed, never truncates a duplicate check.
        var allLongitudes = west < -180 || east > 180;
        var existing = await db.TrashBins.AsNoTracking().Where(b => b.Latitude >= south && b.Latitude <= north &&
                (allLongitudes || b.Longitude >= west && b.Longitude <= east))
            .OrderBy(b => b.Id).Take(MaxExisting + 1)
            .Select(b => new DuplicateRecord(b.Id, b.Name.Length > 200 ? b.Name.Substring(0, 200) : b.Name,
                b.Latitude, b.Longitude, b.DataSource == null ? null : b.DataSource.Name, b.IsApproved)).ToListAsync(ct);
        if (existing.Count > MaxExisting) throw new BinImportException("Območje vsebuje več kot 50.000 obstoječih košev. Razdeli CSV na manjša območja.");
        var grid = new Dictionary<(int, int, int), List<(double Lat, double Lon, ImportCandidate Candidate)>>();
        void Add(double lat, double lon, ImportCandidate candidate)
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180) return;
            var key = Cell(lat, lon);
            if (!grid.TryGetValue(key, out var bucket)) grid[key] = bucket = [];
            bucket.Add((lat, lon, candidate));
        }
        foreach (var bin in existing) Add(bin.Latitude, bin.Longitude, new(bin.Id, null, bin.Name, bin.Source, bin.Active, 0));
        var result = new List<ImportRow>(rows.Count); var comparisons = 0;
        foreach (var row in rows)
        {
            if (row.Status == ImportRowStatus.Invalid) { result.Add(row); continue; }
            var lat = row.Latitude!.Value; var lon = row.Longitude!.Value; var key = Cell(lat, lon);
            var candidates = new List<ImportCandidate>();
            for (var x = -1; x <= 1 && candidates.Count < 3; x++)
                for (var y = -1; y <= 1 && candidates.Count < 3; y++)
                    for (var z = -1; z <= 1 && candidates.Count < 3; z++)
                    {
                        if (!grid.TryGetValue((key.Item1 + x, key.Item2 + y, key.Item3 + z), out var bucket)) continue;
                        foreach (var other in bucket)
                        {
                            if (++comparisons > MaxComparisons) throw new BinImportException("Preveč primerjav dvojnikov. Razdeli CSV na manjša območja.");
                            LastComparisonCount = comparisons;
                            var distance = DuplicateCandidates.Distance(lat, lon, other.Lat, other.Lon);
                            if (distance <= DuplicateCandidates.BinMetres) candidates.Add(other.Candidate with { Metres = distance });
                            if (candidates.Count == 3) break;
                        }
                    }
            result.Add(row with { Candidates = candidates, Status = candidates.Count == 0 ? ImportRowStatus.Ready : ImportRowStatus.PossibleDuplicate });
            // Later rows are conservatively flagged against every earlier valid row, even an excluded duplicate.
            Add(lat, lon, new(null, row.Number, row.Name, null, false, 0));
        }
        return result;
    }
    private static (int, int, int) Cell(double lat, double lon)
    {
        lat *= Math.PI / 180; lon *= Math.PI / 180;
        return ((int)Math.Floor(EarthRadius * Math.Cos(lat) * Math.Cos(lon) / DuplicateCandidates.BinMetres),
            (int)Math.Floor(EarthRadius * Math.Cos(lat) * Math.Sin(lon) / DuplicateCandidates.BinMetres),
            (int)Math.Floor(EarthRadius * Math.Sin(lat) / DuplicateCandidates.BinMetres));
    }

    public async Task<int> ImportAsync(int sourceId, IReadOnlyList<ImportRow> selected, CancellationToken ct = default)
    {
        if (selected.Count is < 1 or > BinImportCsv.MaxRows || selected.Any(r => r.Status != ImportRowStatus.Ready) ||
            selected.Select(r => r.Number).Distinct().Count() != selected.Count) throw new BinImportException("Izberi veljavne pripravljene vrstice.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql())
        {
            // Shared with canonical bin submission/edit/approval and community coordinate review.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(194721, 19)", ct);
        }
        if (!await db.DataSources.AnyAsync(s => s.Id == sourceId, ct)) throw new BinImportException("Izbrani vir ne obstaja več. Ponovi uvoz.");
        var current = await ClassifyAsync(selected, ct);
        if (current.Any(r => r.Status != ImportRowStatus.Ready)) throw new BinImportException("Pojavili so se novi možni dvojniki. Uvoz ni bil izveden; ponovno preveri predogled.");
        var now = clock.GetUtcNow().UtcDateTime;
        db.TrashBins.AddRange(current.Select(row => new TrashBin
        {
            Name = row.Name, Latitude = row.Latitude!.Value, Longitude = row.Longitude!.Value,
            DataSourceId = sourceId, IsApproved = true, DateAdded = now, ApprovedAt = now, UserId = null
        }));
        // Direct curation: no XP, achievements, contributor identity, approval notification or Nearby fan-out.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return current.Count;
    }
}
