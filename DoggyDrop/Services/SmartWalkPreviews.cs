using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

namespace DoggyDrop.Services;

// One preview per owner, ten minutes, 128 retained previews. No disk/cache logging.
public sealed class SmartWalkPreviews(TimeProvider clock) : IDisposable
{
    public sealed class Preview(string token, SmartWalkSelection selection, DateTimeOffset expires)
    {
        public string Token { get; } = token;
        public SmartWalkSelection Selection { get; } = selection;
        public DateTimeOffset Expires { get; } = expires;
        public SemaphoreSlim SaveGate { get; } = new(1, 1);
        public int? SavedPlanId { get; set; }
    }
    private readonly MemoryCache previews = new(new MemoryCacheOptions { SizeLimit = 128 });
    private readonly Dictionary<string, Window> limits = new(StringComparer.Ordinal);
    private readonly HashSet<string> active = [];
    private readonly object gate = new();
    private sealed record Window(DateTimeOffset Start, int Count);
    public bool TryBegin(string owner, out IDisposable? lease)
    {
        lock (gate)
        {
            lease = null; var now = clock.GetUtcNow();
            // A full limiter must fail closed, not silently lose a MemoryCache Set.
            foreach (var expired in limits.Where(p => now >= p.Value.Start.AddMinutes(1)).Select(p => p.Key).ToArray()) limits.Remove(expired);
            limits.TryGetValue(owner, out var window);
            if (window == null && limits.Count >= 4096) return false;
            if (window == null || now >= window.Start.AddMinutes(1)) window = new(now, 0);
            if (active.Contains(owner) || active.Count >= SmartWalkPolicy.MaximumConcurrentGenerations || window.Count >= SmartWalkPolicy.GenerationsPerMinute) return false;
            limits[owner] = window with { Count = window.Count + 1 };
            active.Add(owner); lease = new Lease(() => { lock (gate) active.Remove(owner); }); return true;
        }
    }
    public Preview Put(string owner, SmartWalkSelection selection)
    {
        var preview = new Preview(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)), selection, clock.GetUtcNow().AddMinutes(10));
        previews.Set(owner, preview, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });
        return preview;
    }
    public Preview? Get(string owner, string? token)
    {
        var preview = previews.Get<Preview>(owner);
        if (preview != null && clock.GetUtcNow() >= preview.Expires) { previews.Remove(owner); return null; }
        return token is { Length: 48 } && preview?.Token == token ? preview : null;
    }
    private sealed class Lease(Action release) : IDisposable { private Action? action = release; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
    public void Dispose() { previews.Dispose(); }
}
