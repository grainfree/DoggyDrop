using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// Local Kestrel + SQLite + synthetic walking provider. No Program, credentials,
// network provider, production access or migration execution.
public sealed class SmartWalkHttpTests : IAsyncLifetime
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "smart-walk-" + Guid.NewGuid() + ".db");
    private WebApplication app = null!;
    private readonly SmartWalkTests.FakeRoutes routes = new();
    private readonly TestClock clock = new();
    private sealed class TestClock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public Action? OnRead; public override DateTimeOffset GetUtcNow() { var now = Now; OnRead?.Invoke(); return now; } }
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop", "DoggyDrop.csproj"))) root = root.Parent;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MapController).Assembly.GetName().Name, ContentRootPath = Path.Combine(root!.FullName, "DoggyDrop"), EnvironmentName = "Testing", Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={file};Pooling=False"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider(); builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; }).AddScheme<AuthenticationSchemeOptions, UserAuth>("Test", _ => { });
        builder.Services.AddWalkingRouting(); builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddSingleton<IWalkingRoutes>(routes); builder.Services.AddScoped<SmartWalkPlanner>(); builder.Services.AddSingleton<SmartWalkPreviews>();
        builder.Services.AddSingleton<IGamificationCalendar, GamificationCalendar>(); builder.Services.AddScoped<IGamificationService, GamificationService>();
        builder.Services.AddScoped<INotificationService, NotificationService>(); builder.Services.AddScoped<IWeeklyGoalsService, WeeklyGoalsService>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MapController).Assembly).AddControllersAsServices(); builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp => new WalksController(sp.GetRequiredService<ApplicationDbContext>(), sp.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, sp.GetRequiredService<IGamificationService>(), null!, null!, null!, sp.GetRequiredService<IGamificationCalendar>(), null!));
        app = builder.Build(); app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapGet("/token", (HttpContext c, IAntiforgery a) => a.GetAndStoreTokens(c).RequestToken!);
        app.MapControllers(); app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}"); app.MapRazorPages();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new() { Id = "user", UserName = "user" }, new() { Id = "other", UserName = "other" });
            db.Dogs.AddRange(new() { Id = 1, Name = "Luna", OwnerId = "user" }, new() { Id = 2, Name = "Other", OwnerId = "other" });
            db.Places.Add(new() { Id = 1, Name = "Park <script>alert(1)</script>", Latitude = 46.005, Longitude = 15, Category = PlaceCategory.DogPark }); await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private async Task<HttpClient> Client(string? owner = "user", bool token = true)
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) }; if (owner != null) c.DefaultRequestHeaders.Add("X-Test-User", owner);
        if (token) c.DefaultRequestHeaders.Add("RequestVerificationToken", await c.GetStringAsync("/token")); return c;
    }
    private static object Request(bool destination = false) => new { start = new { latitude = 46, longitude = 15 }, minutes = 30, walkType = destination ? "destination" : "loop", preferences = new[] { "water", "bin", "park" }, variant = 0, placeId = destination ? (int?)1 : null };
    private static FormUrlEncodedContent Form(string token, params (string, string)[] pairs) => new(pairs.Select(p => new KeyValuePair<string, string>(p.Item1, p.Item2)).Append(new("__RequestVerificationToken", token)));
    private static string Token(HttpClient client) => client.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
    [Fact]
    public async Task DefaultPlannerRendersWithoutLocationOrProviderCall()
    {
        using var c = await Client(); var html = await c.GetStringAsync("/Walks/Planner"); Assert.Contains("smartForm", html); Assert.Contains("Napredni planer", html); Assert.DoesNotContain("AI walk", html); Assert.Equal(0, routes.Calls);
        var capture = Environment.GetEnvironmentVariable("DOGGYDROP_SMART_CAPTURE"); if (capture != null) { Directory.CreateDirectory(capture); await File.WriteAllTextAsync(Path.Combine(capture, "smart-initial.html"), html); }
    }
    [Fact]
    public async Task AuthorizationAndAntiforgeryPrecedeProvider()
    {
        using var anon = await Client(null); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/smart-walk", Request())).StatusCode);
        using var noToken = await Client(token: false); Assert.Equal(HttpStatusCode.BadRequest, (await noToken.PostAsJsonAsync("/api/smart-walk", Request())).StatusCode); Assert.Equal(0, routes.Calls);
    }
    [Theory]
    [InlineData("mode=manual")][InlineData("includeWater=false")][InlineData("includeBins=false")]
    [InlineData("includePark=false")][InlineData("includeDogFriendly=false")][InlineData("dogEnergy=high")]
    public async Task ExistingPreferenceBookmarksRemainInAdvancedPlanner(string query)
    {
        using var c = await Client(); var html = await c.GetStringAsync("/Walks/Planner?" + query);
        Assert.Contains("walk-planner-page", html); Assert.DoesNotContain("id=\"smartForm\"", html); Assert.Equal(0, routes.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"start\":{}}")]
    [InlineData("{\"start\":{\"latitude\":46},\"minutes\":30,\"walkType\":\"loop\",\"preferences\":[]}")]
    [InlineData("{\"start\":{\"longitude\":15},\"minutes\":30,\"walkType\":\"loop\",\"preferences\":[]}")]
    [InlineData("{\"start\":{\"latitude\":91,\"longitude\":15},\"minutes\":30,\"walkType\":\"loop\",\"preferences\":[]}")]
    [InlineData("{\"start\":{\"latitude\":46,\"longitude\":15},\"minutes\":90,\"walkType\":\"loop\",\"preferences\":[]}")]
    [InlineData("{\"start\":{\"latitude\":46,\"longitude\":15},\"minutes\":30,\"walkType\":\"loop\",\"preferences\":[\"private\"]}")]
    public async Task MalformedRequestsUseNoProviderOrPreviewBudget(string json)
    {
        using var c = await Client(); var response = await c.PostAsync("/api/smart-walk", new StringContent(json, Encoding.UTF8, "application/json")); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, routes.Calls);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/smart-walk", Request())).StatusCode);
    }
    [Fact]
    public async Task PublicResponseIsAllowlistedAndNoStoreAndNeverPersistsPreview()
    {
        using var c = await Client(); var response = await c.PostAsJsonAsync("/api/smart-walk", Request()); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(new[] { "token", "points", "distanceMeters", "durationSeconds", "facts", "highlights", "notice" }, json.RootElement.EnumerateObject().Select(p => p.Name)); Assert.Equal(3, routes.Calls);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); Assert.Empty(await db.PlannedWalks.ToListAsync()); Assert.Empty(await db.Walks.ToListAsync());
    }
    [Fact]
    public async Task PreviewOwnershipSaveReplayAndHistoricalGeometryArePreserved()
    {
        using var c = await Client(); var response = await c.PostAsJsonAsync("/api/smart-walk", Request()); var result = await response.Content.ReadFromJsonAsync<SmartWalkController.Result>(); Assert.NotNull(result);
        using var other = await Client("other"); await other.PostAsync("/Walks/SmartPlan", Form(Token(other), ("token", result.Token), ("start", "false")));
        await using (var scope = app.Services.CreateAsyncScope()) Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlannedWalks.ToListAsync());
        var save = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result.Token), ("dogId", "1"), ("start", "false"))); Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);
        var again = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result.Token), ("dogId", "1"), ("start", "false"))); Assert.Equal(save.Headers.Location, again.Headers.Location);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var plan = Assert.Single(await db.PlannedWalks.Include(p => p.RoutePoints).ToListAsync()); Assert.Equal(result.Points, plan.RoutePoints!.OrderBy(p => p.Order).Select(p => new[] { p.Latitude, p.Longitude }).ToArray()); Assert.Empty(await db.Walks.ToListAsync());
        }
        var html = await c.GetStringAsync(save.Headers.Location); Assert.Contains("smartSaved", html); Assert.Equal(3, routes.Calls);
        var capture = Environment.GetEnvironmentVariable("DOGGYDROP_SMART_CAPTURE"); if (capture != null) { Directory.CreateDirectory(capture); await File.WriteAllTextAsync(Path.Combine(capture, "smart-saved.html"), html); }
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(save.Headers.Location)).StatusCode);
    }
    [Fact]
    public async Task DeactivatedDestinationBlocksSaveAndNewStartButKeepsStoredPlan()
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request(true))).Content.ReadFromJsonAsync<SmartWalkController.Result>(); Assert.NotNull(result);
        var saved = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result.Token), ("dogId", "1"), ("start", "false"))); Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var place = await db.Places.SingleAsync(); place.IsActive = false; await db.SaveChangesAsync(); }
        var start = await c.PostAsync("/Walks/Start", Form(Token(c), ("dogId", "1"), ("plannedWalkId", "1"))); Assert.Equal(HttpStatusCode.Redirect, start.StatusCode); Assert.Equal(saved.Headers.Location, start.Headers.Location);
        await using (var scope = app.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); Assert.Empty(await db.Walks.ToListAsync()); Assert.Single(await db.PlannedWalks.ToListAsync()); }
    }
    [Fact]
    public async Task ValidStartUsesExistingWalkAndSameSavedGeometry()
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        var start = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("dogId", "1"), ("start", "true"))); Assert.Equal(HttpStatusCode.Redirect, start.StatusCode); Assert.Contains("Active", start.Headers.Location!.ToString());
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var walk = Assert.Single(await db.Walks.ToListAsync()); Assert.Equal("Active", walk.Status); Assert.Equal(1, walk.PlannedWalkId); Assert.Empty(await db.WalkPoints.ToListAsync()); Assert.Equal(3, routes.Calls);
    }
    public async Task DisposeAsync() { if (app != null) await app.DisposeAsync(); File.Delete(file); }
    [Fact]
    public async Task JsonOnlyAndBodyLimitsRejectWithoutProviderUse()
    {
        using var c = await Client(); Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync("/api/smart-walk", new StringContent("start=46,15", Encoding.UTF8, "application/x-www-form-urlencoded"))).StatusCode);
        var oversized = "{\"padding\":\"" + new string('x', 5000) + "\"}"; var response = await c.PostAsync("/api/smart-walk", new StringContent(oversized, Encoding.UTF8, "application/json")); Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest); Assert.Equal(0, routes.Calls);
    }
    [Fact]
    public async Task GenerationLimitRejectsBeforeFourthCandidateSet()
    {
        using var c = await Client(); for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/smart-walk", Request())).StatusCode);
        var fourth = await c.PostAsJsonAsync("/api/smart-walk", Request()); Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode); Assert.NotNull(fourth.Headers.RetryAfter); Assert.Equal(9, routes.Calls);
    }
    [Fact]
    public async Task ForeignDogCannotSaveOrStartOwnedPreview()
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("dogId", "2"), ("start", "true")))).StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlannedWalks.ToListAsync());
    }
    [Theory]
    [InlineData("expired", false)][InlineData("expired", true)]
    [InlineData("unknown", false)][InlineData("unknown", true)]
    [InlineData("other-owner", false)][InlineData("other-owner", true)]
    public async Task UnavailablePreviewFailsClosedWithoutRegeneration(string reason, bool start)
    {
        using var owner = await Client();
        var result = await (await owner.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        if (reason == "expired") clock.Now = clock.Now.AddMinutes(10);
        using var c = await Client(reason == "other-owner" ? "other" : "user");
        var response = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", reason == "unknown" ? new string('F', 48) : result!.Token),
            ("dogId", reason == "other-owner" ? "2" : "1"), ("start", start.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/Walks/Planner", response.Headers.Location!.ToString());
        var html = await c.GetStringAsync(response.Headers.Location); Assert.Contains("Predogled je potekel", WebUtility.HtmlDecode(html));
        if (reason == "expired" && !start && Environment.GetEnvironmentVariable("DOGGYDROP_SMART_CAPTURE") is string capture) await File.WriteAllTextAsync(Path.Combine(capture, "smart-expired.html"), html);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.PlannedWalks.ToListAsync()); Assert.Empty(await db.Walks.ToListAsync()); Assert.Equal(3, routes.Calls);
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, true)][InlineData(false, true)]
    public async Task ConcurrentPreviewSubmissionsCreateOnePlanAndAtMostOneWalk(bool start, bool otherStart)
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        Task<HttpResponseMessage> Submit(bool intent) => c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("dogId", "1"), ("start", intent.ToString()),
            ("distanceMeters", "1"), ("points", "[[0,0],[1,1]]"), ("facts", "fabricated")));
        var responses = await Task.WhenAll(Submit(start), Submit(otherStart)); Assert.All(responses, p => Assert.Equal(HttpStatusCode.Redirect, p.StatusCode));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var plan = Assert.Single(await db.PlannedWalks.Include(p => p.RoutePoints).ToListAsync());
        Assert.Equal(result!.Points, plan.RoutePoints!.OrderBy(p => p.Order).Select(p => new[] { p.Latitude, p.Longitude }).ToArray());
        Assert.Equal(result.DistanceMeters / 1000, plan.EstimatedDistanceKm); Assert.Equal(start || otherStart ? 1 : 0, await db.Walks.CountAsync());
        Assert.Equal(3, routes.Calls); Assert.Empty(await db.WalkPoints.ToListAsync());
    }
    [Fact]
    public async Task RetiredOriginalDestinationCannotBeReplacedByAnotherPlaceAtSameCoordinates()
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request(true))).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        var saved = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("dogId", "1"), ("start", "false")));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var original = await db.Places.SingleAsync();
            original.IsActive = false; original.Latitude = 47;
            db.Places.Add(new() { Name = "Replacement", Latitude = 46.005, Longitude = 15, Category = PlaceCategory.DogPark }); await db.SaveChangesAsync();
        }
        var response = await c.PostAsync("/Walks/Start", Form(Token(c), ("dogId", "1"), ("plannedWalkId", "1")));
        Assert.Equal(saved.Headers.Location, response.Headers.Location);
        if (Environment.GetEnvironmentVariable("DOGGYDROP_SMART_CAPTURE") is string capture) await File.WriteAllTextAsync(Path.Combine(capture, "smart-unavailable.html"), await c.GetStringAsync(response.Headers.Location));
        await using var check = app.Services.CreateAsyncScope(); Assert.Empty(await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Walks.ToListAsync());
    }
    [Fact]
    public async Task IncidentalRetiredWaterIsNotPersistedOrAdvertisedBySavedHistory()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.WaterPoints.Add(new() { Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking }); await db.SaveChangesAsync();
        }
        using var c = await Client();
        var result = await (await c.PostAsJsonAsync("/api/smart-walk", new { start = new { latitude = 46, longitude = 15 }, minutes = 30, walkType = "loop", preferences = Array.Empty<string>() })).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        Assert.Contains(result!.Facts, fact => fact.Kind == "water" && fact.Count == 1);
        await using (var scope = app.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); (await db.WaterPoints.SingleAsync()).IsRetired = true; await db.SaveChangesAsync(); }
        var saved = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("start", "false")));
        var html = await c.GetStringAsync(saved.Headers.Location);
        Assert.Contains("\"facts\":[]", html);
        await using var check = app.Services.CreateAsyncScope(); var plan = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlannedWalks.Include(p => p.Stops).SingleAsync();
        Assert.DoesNotContain(plan.Stops!, s => s.Type == "water"); Assert.Equal(3, routes.Calls);
    }
    [Theory]
    [InlineData(false, false)][InlineData(true, false)]
    [InlineData(false, true)][InlineData(true, true)]
    public async Task ExpiryOrReplacementWhileSubmissionWaitsFailsClosed(bool start, bool replace)
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        var store = app.Services.GetRequiredService<SmartWalkPreviews>(); var preview = store.Get("user", result!.Token)!;
        await preview.SaveGate.WaitAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.OnRead = () => entered.TrySetResult();
        Task<HttpResponseMessage>? pending = null;
        try
        {
            pending = c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result.Token), ("dogId", "1"), ("start", start.ToString())));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); clock.OnRead = null;
            if (replace) store.Put("user", preview.Selection); else clock.Now = clock.Now.AddMinutes(10);
        }
        finally { clock.OnRead = null; preview.SaveGate.Release(); }
        Assert.Equal("/Walks/Planner", (await pending!).Headers.Location!.ToString());
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.PlannedWalks.ToListAsync()); Assert.Empty(await db.Walks.ToListAsync()); Assert.Equal(3, routes.Calls);
    }
    [Theory]
    [InlineData(false, 2, 0)][InlineData(true, 2, 0)][InlineData(true, 3, 1)]
    public async Task ExpiryDuringDatabasePhasePreventsFurtherMutation(bool start, int expireAfterRead, int expectedSaved)
    {
        using var c = await Client(); var result = await (await c.PostAsJsonAsync("/api/smart-walk", Request())).Content.ReadFromJsonAsync<SmartWalkController.Result>();
        var reads = 0;
        // Return the old clock value for the admission check, then expire before
        // the following database phase. No sleeps or application test hooks.
        clock.OnRead = () => { if (++reads == expireAfterRead) clock.Now = clock.Now.AddMinutes(10); };
        var response = await c.PostAsync("/Walks/SmartPlan", Form(Token(c), ("token", result!.Token), ("dogId", "1"), ("start", start.ToString())));
        clock.OnRead = null; Assert.Equal("/Walks/Planner", response.Headers.Location!.ToString());
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(expectedSaved, await db.PlannedWalks.CountAsync()); Assert.Empty(await db.Walks.ToListAsync()); Assert.Equal(3, routes.Calls);
    }
    [Theory]
    [InlineData("/Walks/SmartPlan")][InlineData("/Walks/Start")][InlineData("/api/smart-walk")]
    public async Task MissingAndInvalidAntiforgeryRejectAllBrowserMutations(string path)
    {
        using var c = await Client(token: false);
        foreach (var invalid in new[] { "", "invalid-token" })
        {
            c.DefaultRequestHeaders.Remove("RequestVerificationToken"); if (invalid.Length > 0) c.DefaultRequestHeaders.Add("RequestVerificationToken", invalid);
            using var response = path.StartsWith("/api/") ? await c.PostAsJsonAsync(path, Request()) : await c.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string> { ["dogId"] = "1" }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(0, routes.Calls);
        await using var scope = app.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Walks.ToListAsync());
    }
    private sealed class UserAuth(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() { var id = Request.Headers["X-Test-User"].ToString(); if (id is not ("user" or "other")) return Task.FromResult(AuthenticateResult.NoResult()); return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Name, id)], IdentityConstants.ApplicationScheme)), Scheme.Name))); }
    }
}
