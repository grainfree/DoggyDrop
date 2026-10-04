using System.Net;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DoggyDrop.Tests;

// Loopback-only host. Never runs Program, DB startup, email or external providers.
public sealed class LaunchReadinessTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop.sln"))) root = root.Parent;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            EnvironmentName = "Production", ContentRootPath = Path.Combine(root!.FullName, "DoggyDrop"), Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHsts(o => o.ExcludedHosts.Clear());
        app = builder.Build();
        app.UseMiddleware<LaunchHeaders>();
        app.UseExceptionHandler(error => error.Run(async context => {
            context.Response.StatusCode = 500;
            if (LaunchReadiness.WantsHtml(context)) await LaunchReadiness.WriteStatus(context);
            else await context.Response.WriteAsJsonAsync(new { error = "Prišlo je do napake." });
        }));
        app.Use(async (ctx, next) => { if (ctx.Request.Headers.ContainsKey("X-Test-Https")) ctx.Request.Scheme = "https"; await next(); });
        app.UseHsts();
        app.UseStatusCodePages(async status => {
            if (status.HttpContext.Response.StatusCode is 403 or 404 && LaunchReadiness.WantsHtml(status.HttpContext))
                await LaunchReadiness.WriteStatus(status.HttpContext);
        });
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = LaunchReadiness.StaticResponse });
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/failure", (Func<string>)(() => throw new InvalidOperationException("PRIVATE_ERROR_DETAIL")));
        app.MapGet("/denied", () => Results.StatusCode(403));
        app.MapGet("/Identity/Account/ResetPassword", (HttpContext c) => {
            c.Response.Headers["Referrer-Policy"] = "no-referrer"; c.Response.Headers.CacheControl = "no-store";
            return Results.Content("token form", "text/html");
        });
        foreach (var path in new[] { "/Home/UserProfile", "/Dogs", "/Walks", "/SavedPlans", "/BinContributions/Mine", "/AdminBins", "/api/private", "/Walks/Smart" })
            app.MapGet(path, () => Results.Content("synthetic private response", "text/html"));
        await app.StartAsync();
        client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    }
    [Theory]
    [InlineData("/Home/UserProfile")][InlineData("/Dogs")][InlineData("/Walks")][InlineData("/SavedPlans")]
    [InlineData("/BinContributions/Mine")][InlineData("/AdminBins")][InlineData("/api/private")][InlineData("/Walks/Smart")]
    public async Task DynamicResponsesAreNeverSharedOrStored(string path)
    {
        var r = await client.GetAsync(path);
        Assert.True(r.Headers.CacheControl!.NoStore); Assert.True(r.Headers.CacheControl.Private); Assert.True(r.Headers.CacheControl.NoCache);
        Assert.Contains(r.Headers.Pragma, x => x.Name == "no-cache");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
    }
    [Fact] public async Task IdentityRetainsStrongerReferrerAndCachePolicy()
    {
        var r = await client.GetAsync("/Identity/Account/ResetPassword");
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single()); Assert.True(r.Headers.CacheControl!.NoStore);
    }
    [Fact] public async Task HeadersAreBoundedAndPermitRequiredCapabilities()
    {
        var r = await client.GetAsync("/health");
        Assert.Equal("base-uri 'self'; object-src 'none'; frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("strict-origin-when-cross-origin",r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("geolocation=(self), camera=(self), microphone=(), payment=(), usb=()", r.Headers.GetValues("Permissions-Policy").Single());
        Assert.False(r.Headers.Contains("Strict-Transport-Security"));
        using var request = new HttpRequestMessage(HttpMethod.Get,"/health");request.Headers.Add("X-Test-Https","1");
        var https = await client.SendAsync(request); Assert.Contains("max-age=",https.Headers.GetValues("Strict-Transport-Security").Single());
    }
    [Theory][InlineData("/missing",404)][InlineData("/denied",403)][InlineData("/failure",500)]
    public async Task HtmlErrorsAreBrandedGenericAndRetainStatus(string path,int status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,path);request.Headers.Accept.ParseAdd("text/html");
        var r=await client.SendAsync(request);var html=await r.Content.ReadAsStringAsync();
        Assert.Equal(status,(int)r.StatusCode);Assert.Contains("DoggyDrop",html);Assert.Contains("Nazaj na zemljevid",html);
        Assert.DoesNotContain("PRIVATE_ERROR_DETAIL",html);Assert.DoesNotContain("Exception",html);Assert.Contains("noindex",html);
        Assert.True(r.Headers.CacheControl!.NoStore);
    }
    [Fact] public async Task ApiErrorsDoNotTurnIntoSuccessfulHtml()
    {
        var r=await client.GetAsync("/failure"); Assert.Equal(HttpStatusCode.InternalServerError,r.StatusCode);
        Assert.Equal("application/json",r.Content.Headers.ContentType!.MediaType);Assert.DoesNotContain("PRIVATE_ERROR_DETAIL",await r.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/missing")).StatusCode);
    }
    [Fact] public async Task HealthRemainsMinimal()
    {
        var r=await client.GetAsync("/health");Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("{\"status\":\"ok\"}",await r.Content.ReadAsStringAsync());
    }
    [Theory][InlineData("/sw.js?v=arbitrary")][InlineData("/manifest.json?v=arbitrary")][InlineData("/offline.html")][InlineData("/js/pwa.js")]
    public async Task UnversionedAndWorkerAssetsRevalidate(string path)
    { var r=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(r.Headers.CacheControl!.NoCache);Assert.DoesNotContain(r.Headers.CacheControl.Extensions,x=>x.Name=="immutable"); }
    [Fact] public async Task VersionedStaticAssetHasLongLifetime()
    { var r=await client.GetAsync("/js/pwa.js?v=synthetic-content-hash");Assert.True(r.Headers.CacheControl!.Public);Assert.Equal(TimeSpan.FromDays(365),r.Headers.CacheControl.MaxAge); }
    public async Task DisposeAsync() { client.Dispose();await app.StopAsync();await app.DisposeAsync(); }
}
