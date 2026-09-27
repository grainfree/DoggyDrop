namespace DoggyDrop.Services;

// Instance-local, bounded temporary state. Restarts / another instance require a fresh upload.
// No uploaded file, unused columns, or row contents are written to disk or logs by this store.
public sealed class BinImportSessions : IDisposable
{
    private readonly Dictionary<string, BinImportSession> sessions = [];
    private readonly object sync = new();
    private readonly TimeProvider clock;
    private readonly ITimer timer;
    public SemaphoreSlim UploadGate { get; } = new(2, 2);
    public const long MaxMemory = 64 * 1024 * 1024;
    public BinImportSessions(TimeProvider clock)
    {
        this.clock = clock;
        timer = clock.CreateTimer(_ => { lock (sync) Prune(); }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    private void Prune()
    {
        foreach (var id in sessions.Where(x => x.Value.ExpiresAt <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) sessions.Remove(id);
    }
    public BinImportSession Add(string owner, string filename, ImportSource source, ImportCsv csv)
    {
        // Conservative overhead allowance also reserves space for mapped rows and duplicate context.
        var size = 2L * 1024 * 1024 + csv.Rows.Sum(r => r.Sum(c => 64L + c.Length * 2L));
        lock (sync)
        {
            Prune();
            // Completed summaries retain replay safety cheaply; evict them first when a new upload needs capacity.
            foreach (var completed in sessions.Values.Where(s => s.Result != null).OrderBy(s => s.ExpiresAt).ToArray())
            {
                if (sessions.Count < 8 && sessions.Values.Sum(s => s.ReservedBytes) + size <= MaxMemory) break;
                sessions.Remove(completed.Id);
            }
            if (sessions.Count >= 8 || sessions.Values.Count(s => s.Owner == owner && s.Result == null) >= 2 || sessions.Values.Sum(s => s.ReservedBytes) + size > MaxMemory)
                throw new BinImportException("Začasni prostor je zaseden. Zaključi ali prekliči prejšnji uvoz oziroma poskusi pozneje.");
            var session = new BinImportSession(Guid.NewGuid().ToString("N"), owner, filename, source, clock.GetUtcNow().AddMinutes(30), csv, size);
            sessions.Add(session.Id, session); return session;
        }
    }
    public BinImportSession? Find(string? id, string owner)
    {
        lock (sync)
        {
            Prune();
            return id != null && sessions.TryGetValue(id, out var session) && session.Owner == owner ? session : null;
        }
    }
    public void Remove(string id, string owner)
    {
        lock (sync) { if (sessions.TryGetValue(id, out var session) && session.Owner == owner) sessions.Remove(id); }
    }
    public void Dispose() { timer.Dispose(); UploadGate.Dispose(); }
}

public sealed class BinImportSession(string id, string owner, string fileName, ImportSource source, DateTimeOffset expiresAt, ImportCsv csv, long size)
{
    public string Id { get; } = id;
    public string Owner { get; } = owner;
    public string FileName { get; } = fileName;
    public ImportSource Source { get; } = source;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public long Size { get; } = size;
    public long ReservedBytes => Result == null ? Size : 4096;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public ImportCsv? Csv { get; set; } = csv;
    public IReadOnlyList<ImportRow> Rows { get; set; } = [];
    public HashSet<int> Selected { get; set; } = [];
    public int Version { get; set; }
    public int? ConfirmedVersion { get; set; }
    public ImportResult? Result { get; set; }
    public ImportPage Page(int page = 1) => new(Id, Version, FileName, Source, ExpiresAt, Csv?.Headers,
        Csv == null ? null : ImportMapping.Suggest(Csv.Headers), Rows.Skip((page - 1) * 100).Take(100).ToArray(),
        Selected.ToHashSet(), page, Rows.Count, Rows.Count(r => r.Status == ImportRowStatus.Ready),
        Rows.Count(r => r.Status == ImportRowStatus.PossibleDuplicate), Rows.Count(r => r.Status == ImportRowStatus.Invalid), Result);
}
