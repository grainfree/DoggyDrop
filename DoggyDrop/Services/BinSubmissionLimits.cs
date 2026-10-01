using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DoggyDrop.Services;

// Bounded, process-local protection; no persistent tracking or coordinate logging.
public sealed class BinSubmissionLimits(TimeProvider clock)
{
    public static readonly BinSubmissionLimits Shared = new(TimeProvider.System);
    private readonly Dictionary<string, (DateTimeOffset Start, int Count)> windows = new();
    public bool Take(string key)
    {
        lock (windows)
        {
            var now = clock.GetUtcNow();
            foreach (var stale in windows.Where(p => now - p.Value.Start >= TimeSpan.FromHours(1)).Select(p => p.Key).ToArray()) windows.Remove(stale);
            if (!windows.TryGetValue(key, out var window))
            {
                if (windows.Count >= 4096) return false;
                window = (now, 0);
            }
            if (window.Count >= 10) return false;
            windows[key] = (window.Start, window.Count + 1); return true;
        }
    }
}

public sealed class BinSubmissionLimitAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var http = context.HttpContext;
        var key = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (!(http.RequestServices.GetService<BinSubmissionLimits>() ?? BinSubmissionLimits.Shared).Take(key))
        {
            http.Response.Headers.RetryAfter = "3600";
            context.Result = new ObjectResult("Preveč prispevkov. Poskusi pozneje.") { StatusCode = 429 };
        }
    }
    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
