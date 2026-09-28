namespace DoggyDrop.Services;

// Register before forwarded headers and exception handling. Capture the incoming
// authority only to suppress indexing on alternate hosts, never to construct URLs.
public sealed class SeoIndexingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, IHostEnvironment environment)
    {
        context.Items[SeoMetadata.RawHostKey] = context.Request.Host.Value;
        var site = new SeoSite(configuration, environment);
        context.Response.OnStarting(() => {
            var response = context.Response;
            var publicAsset = response.StatusCode == 200 &&
                (response.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true ||
                 response.ContentType?.StartsWith("text/css", StringComparison.OrdinalIgnoreCase) == true ||
                 response.ContentType?.Contains("javascript", StringComparison.OrdinalIgnoreCase) == true ||
                 response.ContentType?.StartsWith("font/", StringComparison.OrdinalIgnoreCase) == true);
            if (!publicAsset)
                response.Headers["X-Robots-Tag"] = response.StatusCode == 200 &&
                    site.AllowsIndexing(context) && context.Items[SeoMetadata.IndexableKey] is true &&
                    (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                        ? "index, follow" : "noindex, nofollow";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
