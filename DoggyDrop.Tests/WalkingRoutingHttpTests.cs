using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

// Synthetic local host; never invokes Program, migrations or any provider.
public sealed class WalkingRoutingHttpTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private readonly Routes routes = new();
    private sealed class Routes : IWalkingRoutes
    {
        public int Calls;
        public IWalkingRoutes? Inner;
        public WalkingRouteResult Result = new([new(46.56, 15.64), new(46.561, 15.641)], 245.5, 189);
        public Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points, CancellationToken ct = default)
        { Calls++; return Inner?.RouteAsync(points, ct) ?? Task.FromResult(Result); }
    }
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop", "DoggyDrop.csproj"))) root = root.Parent;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MapController).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root!.FullName, "DoggyDrop"), EnvironmentName = "Production", Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddWalkingRouting(); builder.Services.AddSingleton<IWalkingRoutes>(routes);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MapController).Assembly).AddApplicationPart(typeof(WalkingFixtureController).Assembly);
        builder.Services.AddRazorPages();
        app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers(); app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}"); app.MapRazorPages();
        await app.StartAsync();
    }
    private async Task<HttpClient> Client(bool token = true)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        if (token) client.DefaultRequestHeaders.Add("RequestVerificationToken", await client.GetStringAsync("/token"));
        return client;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client) => client.PostAsJsonAsync("/api/walking-route",
        new { origin = new { latitude = 46.56, longitude = 15.64 }, destination = new { latitude = 46.561, longitude = 15.641 } });
    [Fact]
    public async Task AnonymousPostRequiresAntiforgeryAndReturnsOnlyPublicRouteMetrics()
    {
        using var anonymous = await Client(false);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(anonymous)).StatusCode); Assert.Equal(0, routes.Calls);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await anonymous.GetAsync("/api/walking-route")).StatusCode);
        using var client = await Client(); using var response = await Post(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "points", "distanceMeters", "durationSeconds" }, json.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(245.5, json.RootElement.GetProperty("distanceMeters").GetDouble()); Assert.Equal(189, json.RootElement.GetProperty("durationSeconds").GetDouble());
    }
    [Theory] [InlineData("{}")] [InlineData("null")] [InlineData("bad json")]
    [InlineData("{\"origin\":{\"latitude\":91,\"longitude\":0},\"destination\":{\"latitude\":46,\"longitude\":15}}")]
    public async Task InvalidInputDoesNotReachProvider(string body)
    {
        using var client = await Client(); using var response = await client.PostAsync("/api/walking-route", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, routes.Calls);
    }
    private sealed class RecordingProvider : HttpMessageHandler
    {
        public int Calls;
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(WalkingRoutingTests.ValidJson, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private RecordingProvider UseMockedProvider()
    {
        var provider = new RecordingProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["OpenRouteService:ApiKey"] = "test-only-key" }).Build();
        routes.Inner = new OrsWalkingRoutes(new HttpClient(provider), configuration,
            app.Services.GetRequiredService<WalkingRouteBudget>(), NullLogger<OrsWalkingRoutes>.Instance);
        return provider;
    }

    private void AssertProviderBudgetUntouched()
    {
        var budget = app.Services.GetRequiredService<WalkingRouteBudget>();
        for (var attempt = 0; attempt < 30; attempt++) Assert.True(budget.TryTake(out _));
        Assert.False(budget.TryTake(out _));
    }

    [Theory]
    [InlineData("origin", "missing")] [InlineData("origin", "empty")]
    [InlineData("origin", "latitude-only")] [InlineData("origin", "longitude-only")]
    [InlineData("destination", "missing")] [InlineData("destination", "empty")]
    [InlineData("destination", "latitude-only")] [InlineData("destination", "longitude-only")]
    [InlineData("origin", "null")] [InlineData("destination", "null")]
    public async Task IncompleteCoordinatesAreRejectedBeforeRoutingOrProviderBudget(string member, string shape)
    {
        var provider = UseMockedProvider();
        var body = new Dictionary<string, object?> {
            ["origin"] = new { latitude = 46.56, longitude = 15.64 },
            ["destination"] = new { latitude = 46.561, longitude = 15.641 }
        };
        if (shape == "missing") body.Remove(member);
        else body[member] = shape switch {
            "empty" => new Dictionary<string, double>(),
            "latitude-only" => new { latitude = 46.56 },
            "longitude-only" => new { longitude = 15.64 },
            _ => null
        };
        using var client = await Client();
        using var response = await client.PostAsJsonAsync("/api/walking-route", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, routes.Calls);
        Assert.Equal(0, provider.Calls);
        AssertProviderBudgetUntouched();
    }

    [Theory]
    [InlineData(0, 15, 0.001, 15.001)]
    [InlineData(46, 0, 46.001, 0.001)]
    [InlineData(0, 0, 0.001, 0.001)]
    [InlineData(0.001, 0.001, 0, 0)]
    public async Task ExplicitZeroMembersReachProviderWithoutBeingTreatedAsMissing(double lat, double lng, double destinationLat, double destinationLng)
    {
        var provider = UseMockedProvider();
        using var client = await Client();
        using var response = await client.PostAsJsonAsync("/api/walking-route", new {
            origin = new { latitude = lat, longitude = lng },
            destination = new { latitude = destinationLat, longitude = destinationLng }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, routes.Calls); Assert.Equal(1, provider.Calls);
        using var body = JsonDocument.Parse(provider.Body!);
        var coordinates = body.RootElement.GetProperty("coordinates");
        Assert.Equal(lng, coordinates[0][0].GetDouble()); Assert.Equal(lat, coordinates[0][1].GetDouble());
        Assert.Equal(destinationLng, coordinates[1][0].GetDouble()); Assert.Equal(destinationLat, coordinates[1][1].GetDouble());
    }

    [Fact]
    public async Task IdenticalExplicitZeroEndpointsStillFollowExistingDistinctPointPolicy()
    {
        var provider = UseMockedProvider();
        using var client = await Client();
        using var response = await client.PostAsJsonAsync("/api/walking-route", new {
            origin = new { latitude = 0, longitude = 0 }, destination = new { latitude = 0, longitude = 0 }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, routes.Calls); Assert.Equal(0, provider.Calls);
        AssertProviderBudgetUntouched();
    }

    [Theory] [InlineData("unavailable", 503)] [InlineData("busy", 429)] [InlineData("invalid", 400)]
    public async Task ProviderFailureIsExplicitWithNoGeometry(string reason, int status)
    {
        routes.Result = WalkingRouteResult.Unavailable(reason, reason == "busy" ? 120 : 0);
        using var client = await Client(); using var response = await Post(client);
        Assert.Equal(status, (int)response.StatusCode); Assert.DoesNotContain("points", await response.Content.ReadAsStringAsync());
        if (reason == "busy") Assert.Equal(TimeSpan.FromSeconds(120), response.Headers.RetryAfter!.Delta);
    }
    [Fact]
    public async Task EndpointLimitsAnonymousRequestsBeforeCallingProvider()
    {
        using var client = await Client();
        for (var n = 0; n < 10; n++) Assert.Equal(HttpStatusCode.OK, (await Post(client)).StatusCode);
        using var response = await Post(client); Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter); Assert.Equal(10, routes.Calls);
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task RealPlannerRazorDistinguishesRoutedAndApproximateGeometry(bool routed)
    {
        using var client = await Client(false); var html = await client.GetStringAsync("/walking-fixture/" + routed.ToString().ToLowerInvariant());
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains(routed ? "Peš · razdalja po poti" : "Približna geometrijska razdalja", text);
        Assert.Contains(routed ? "~4 min hoje" : WalkingPlanRouting.ApproximateNotice, text);
        Assert.Equal(routed, text.Contains("© openrouteservice.org by HeiGIT"));
        var capture = Environment.GetEnvironmentVariable("DOGGYDROP_WALKING_CAPTURE");
        if (!string.IsNullOrWhiteSpace(capture)) { Directory.CreateDirectory(capture); await File.WriteAllTextAsync(Path.Combine(capture, routed ? "planner-routed.html" : "planner-approximate.html"), html); }
    }
    public async Task DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
}

public sealed class WalkingFixtureController : Controller
{
    [HttpGet("/walking-fixture/{routed:bool}")]
    public IActionResult Planner(bool routed) => View("~/Views/Walks/Planner.cshtml", new WalkPlannerViewModel {
        UsesCurrentLocation = true, Latitude = 46.56, Longitude = 15.64,
        Dogs = [new Dog { Id = 1, Name = "Luna" }], SelectedDogId = 1,
        Route = new PlannedWalkRoute { IsWalkingRoute = routed, EstimatedDistanceKm = 0.2455, EstimatedMinutes = routed ? 4 : 0,
            Summary = routed ? "Pešpot je izračunana z OpenRouteService; upoštevaj označbe in razmere na terenu." : WalkingPlanRouting.ApproximateNotice,
            RoutePoints = [new() { Latitude = 46.56, Longitude = 15.64 }, new() { Latitude = 46.561, Longitude = 15.641 }, new() { Latitude = 46.56, Longitude = 15.64 }],
            Stops = [new() { Order = 1, Name = "Začetek", Type = "start", Latitude = 46.56, Longitude = 15.64 }, new() { Order = 2, Name = "Testni koš", Type = "bin", Latitude = 46.561, Longitude = 15.641 }, new() { Order = 3, Name = "Cilj", Type = "finish", Latitude = 46.56, Longitude = 15.64 }] }
    });
}
