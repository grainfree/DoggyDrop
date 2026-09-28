using DoggyDrop.Models;

namespace DoggyDrop.Services;

// Same instance-local contract as the Bin importer, with a separate bounded budget.
public sealed class PlaceImportSessions : IDisposable
{
    public const long MaxMemory = 64 * 1024 * 1024;
    private readonly Dictionary<string, PlaceImportSession> sessions = [];
    private readonly object sync = new();
    private readonly TimeProvider clock;
    private readonly ITimer timer;
    public SemaphoreSlim UploadGate { get; } = new(2, 2);
    public PlaceImportSessions(TimeProvider clock)
    {
        this.clock = clock;
        timer = clock.CreateTimer(_ => { lock (sync) Prune(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    private void Prune()
    {
        foreach (var id in sessions.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) sessions.Remove(id);
    }
    public PlaceImportSession Add(string owner, string filename, ImportSource source, PlaceCategory? category, ImportCsv csv)
    {
        if (category.HasValue && !PlaceCategories.IsSupported(category.Value)) throw new BinImportException("Izberi podprto kategorijo.");
        // Reserve raw CSV, trimmed/mapped text, bounded candidate context and session overhead.
        var size = 8L * 1024 * 1024 + csv.Rows.Sum(r => r.Sum(c => 96L + c.Length * 6L));
        lock (sync)
        {
            Prune();
            foreach (var done in sessions.Values.Where(s => s.Result != null).OrderBy(s => s.ExpiresAt).ToArray())
            {
                if (sessions.Count < 8 && sessions.Values.Sum(s => s.ReservedBytes) + size <= MaxMemory) break;
                sessions.Remove(done.Id);
            }
            if (sessions.Count >= 8 || sessions.Values.Count(s => s.Owner == owner && s.Result == null) >= 2 || sessions.Values.Sum(s => s.ReservedBytes) + size > MaxMemory)
                throw new BinImportException("Začasni prostor je zaseden. Zaključi ali prekliči prejšnji uvoz oziroma razdeli CSV.");
            var session = new PlaceImportSession(Guid.NewGuid().ToString("N"), owner, filename, source, category, clock.GetUtcNow().AddMinutes(30), csv, size);
            sessions.Add(session.Id, session); return session;
        }
    }
    public PlaceImportSession? Find(string? id, string owner)
    {
        lock (sync) { Prune(); return id != null && sessions.TryGetValue(id, out var s) && s.Owner == owner ? s : null; }
    }
    public void Remove(string id, string owner)
    {
        lock (sync) { if (sessions.TryGetValue(id, out var s) && s.Owner == owner) sessions.Remove(id); }
    }
    public void Dispose() { timer.Dispose(); UploadGate.Dispose(); }
}

public sealed class PlaceImportSession(string id, string owner, string filename, ImportSource source, PlaceCategory? category,
    DateTimeOffset expiresAt, ImportCsv csv, long size)
{
    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public string FileName { get; } = filename;
    public ImportSource Source { get; } = source;
    public PlaceCategory? FixedCategory { get; } = category;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public long ReservedBytes => Result == null ? size : 4096;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public ImportCsv? Csv { get; set; } = csv;
    public IReadOnlyList<PlaceImportRow> Rows { get; set; } = [];
    public HashSet<int> Selected { get; set; } = [];
    public int Version { get; set; }
    public int? ConfirmedVersion { get; set; }
    public ImportResult? Result { get; set; }
    public PlaceImportPage Page(int page = 1) => new(Id, Version, FileName, Source, FixedCategory, ExpiresAt,
        Csv?.Headers, Csv == null ? null : PlaceImportMapping.Suggest(Csv.Headers), Rows.Skip((page - 1) * 100).Take(100).ToArray(),
        Selected.ToHashSet(), page, Rows.Count, Rows.Count(r => r.Status == ImportRowStatus.Ready),
        Rows.Count(r => r.Status == ImportRowStatus.PossibleDuplicate), Rows.Count(r => r.Status == ImportRowStatus.Invalid), Result);
}
