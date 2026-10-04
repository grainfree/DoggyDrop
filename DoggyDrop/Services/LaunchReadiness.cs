using Microsoft.AspNetCore.Diagnostics;

namespace DoggyDrop.Services;

// No authentication decisions or data access. Dynamic content is never cacheable,
// including anonymous HTML that may contain antiforgery or later become personal.
public sealed class LaunchHeaders(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() => {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            // Deliberately limited: inline Razor/event handlers prevent a strong script CSP.
            // Do not claim this constrains scripts or third-party network connections.
            h["Content-Security-Policy"] = "base-uri 'self'; object-src 'none'; frame-ancestors 'none'";
            h["X-Frame-Options"] = "DENY";
            h["Permissions-Policy"] = "geolocation=(self), camera=(self), microphone=(), payment=(), usb=()";
            if (!h.ContainsKey("Referrer-Policy"))
                h["Referrer-Policy"] = context.Request.Path.StartsWithSegments("/Identity") ? "no-referrer" : "strict-origin-when-cross-origin";
            if (!context.Items.ContainsKey(LaunchReadiness.StaticAssetKey))
            {
                h.CacheControl = "private, no-store, no-cache";
                h.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        });
        await next(context);
    }
}

public static class LaunchReadiness
{
    public const string StaticAssetKey = "DoggyDrop.StaticAsset";
    public static void StaticResponse(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext context)
    {
        context.Context.Items[StaticAssetKey] = true;
        // Only a content-versioned static URL gets a long lifetime. Never cache the
        // worker or manifest indefinitely, even if a caller invents a query string.
        var path = context.Context.Request.Path.Value ?? "";
        var versioned = context.Context.Request.Query.ContainsKey("v") &&
            !path.Equals("/sw.js", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/manifest.json", StringComparison.OrdinalIgnoreCase) &&
            !path.Equals("/offline.html", StringComparison.OrdinalIgnoreCase);
        context.Context.Response.Headers.CacheControl = versioned ? "public, max-age=31536000, immutable" : "no-cache";
    }

    public static bool WantsHtml(HttpContext context) => HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Headers.Accept.Any(x => x?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

    public static async Task WriteStatus(HttpContext context)
    {
        var status = context.Response.StatusCode;
        context.Response.Headers.CacheControl = "private, no-store, no-cache";
        context.Response.ContentType = "text/html; charset=utf-8";
        var title = status == 404 ? "Te strani ni mogoče najti." : status == 403 ? "Za to stran nimaš dovoljenja." : "Prišlo je do napake.";
        // No request path, exception, user or provider details enter this response.
        await context.Response.WriteAsync($"""
            <!doctype html><html lang="sl"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex, nofollow"><title>DoggyDrop – {status}</title>
            <link rel="stylesheet" href="/css/pwa.css"></head><body class="launch-document">
            <main><p class="launch-brand">DoggyDrop</p><h1>{title}</h1>
            <p>Poskusi znova ali se vrni na zemljevid.</p><a href="/">Nazaj na zemljevid</a></main></body></html>
            """);
    }
}
