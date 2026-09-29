using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// Local synthetic MVC host only. Never invokes Program, migrations or live storage.
public sealed class PlaceMapPopupHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"popup-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop", "DoggyDrop.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(MapController).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root.FullName, "DoggyDrop"), EnvironmentName = "Production", Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Seo:PublicOrigin"] = "https://doggydrop.app";
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddScoped<IPlaceLogoStorage, MissingPlaceLogoStorage>();
        builder.Services.AddScoped<IPlaceLogoReferenceReader, PlaceLogoReferenceReader>();
        builder.Services.AddSingleton<IGamificationCalendar, GamificationCalendar>();
        builder.Services.AddScoped<IGamificationService, GamificationService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddScoped<IWeeklyGoalsService, WeeklyGoalsService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; })
            .AddScheme<AuthenticationSchemeOptions, TestUser>("Test", _ => { });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MapController).Assembly).AddControllersAsServices();
        builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp => new MapController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IWebHostEnvironment>(), sp.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, null!,
            sp.GetRequiredService<IGamificationService>(), null!, null!, null!, sp.GetRequiredService<IGamificationCalendar>(), null!, placeLogoCloud: new("test")));
        app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}"); app.MapRazorPages();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = "admin", UserName = "Admin" }, new ApplicationUser { Id = "user", UserName = "User" });
            db.Places.AddRange(new Place { Id = 1, Name = "Mr.Pet Test", Category = PlaceCategory.PetShop, Latitude = 46, Longitude = 15,
                Address = "Naslov 1", LogoUrl = BulkPlaceLogoTests.Url('a'), AmenitiesSourceUrl = "https://private.invalid/proof", AmenitiesVerifiedAt = DateTime.UtcNow },
                new Place { Id = 2, Name = "Inactive", Category = PlaceCategory.PetShop, IsActive = false },
                new Place { Id = 3, Name = "Unsupported", Category = (PlaceCategory)999 },
                new Place { Id = 4, Name = "Park", Category = PlaceCategory.DogPark, Latitude = 46.01, Longitude = 15.01, LogoUrl = BulkPlaceLogoTests.Url('a') });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user = null)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        if (user != null) client.DefaultRequestHeaders.Add("X-Test-User", user);
        return client;
    }
    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{name}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<string> Page(HttpClient client, string path)
    { var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadAsStringAsync(); }
    private static JsonDocument Places(string html) => JsonDocument.Parse(Regex.Match(html, @"const managedPlaces = \(([^\r\n]+)\)\.filter").Groups[1].Value);
    private static FormUrlEncodedContent Form(string html, params (string Key, string Value)[] changes)
    {
        var fields = new Dictionary<string, string> { ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken"),
            ["OriginalUpdatedAt"] = Field(html, "OriginalUpdatedAt"), ["Name"] = "Mr.Pet Test", ["Category"] = "PetShop",
            ["Latitude"] = "46", ["Longitude"] = "15" };
        foreach (var (key, value) in changes) fields[key] = value;
        // Submit the invariant-number markers emitted by ASP.NET's real number inputs.
        var invariant = Regex.Matches(html, "name=\"__Invariant\"[^>]*value=\"([^\"]+)\"")
            .Select(match => new KeyValuePair<string, string>("__Invariant", WebUtility.HtmlDecode(match.Groups[1].Value)));
        return new FormUrlEncodedContent(fields.Concat(invariant));
    }
    [Theory] [InlineData(null, false)] [InlineData("user", false)] [InlineData("admin", true)]
    public async Task MapOffersQuickEditOnlyToAdminAndProjectsOnlyPublicFields(string? user, bool admin)
    {
        using var client = Client(user); var response = await client.GetAsync("/"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore); var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(admin, html.Contains("id=\"placeMapAdminAction\""));
        Assert.Equal(admin, html.Contains("data-edit-base=\"/AdminPlaces/Edit\""));
        if (!admin) Assert.DoesNotContain("/AdminPlaces/Edit", html);
        using var places = Places(html); var rows = places.RootElement.EnumerateArray().ToArray();
        Assert.Equal(new[] { 1, 4 }, rows.Select(p => p.GetProperty("id").GetInt32()));
        Assert.Equal(SeoMetadata.PlacePath(1, "Mr.Pet Test"), rows[0].GetProperty("detailsUrl").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("logoUrl").ValueKind);
        Assert.DoesNotContain("private.invalid", html);
        foreach (var row in rows) Assert.Equal(new[] { "address", "category", "categoryKey", "categoryLabel", "detailsUrl", "iconClass", "id",
            "isCommercial", "isCurrentlyFeatured", "latitude", "logoUrl", "longitude", "name" }, row.EnumerateObject().Select(p => p.Name).Order());
        await Capture(admin ? "home-admin" : user == null ? "home-anonymous" : "home-user", html);
    }
    [Fact]
    public async Task ExistingEditRemainsAdminOnlyAndAntiforgeryProtected()
    {
        using var anon = Client(); using var user = Client("user"); using var admin = Client("admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/AdminPlaces/Edit/1?returnTo=map")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/AdminPlaces/Edit/1?returnTo=map")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent([]))).StatusCode);
    }
    [Theory]
    [InlineData(null, "osm")]
    [InlineData("stadia", "stadia")]
    [InlineData("carto", "osm")]
    [InlineData("https://untrusted.invalid/?private=value", "osm")]
    public async Task HomeRendersOnlyAllowlistedBasemapConfiguration(string? configured, string expected)
    {
        app.Configuration["Basemap:Provider"] = configured;
        using var client = Client();
        var html = await Page(client, "/");
        Assert.Contains($"data-provider=\"{expected}\"", html);
        Assert.DoesNotContain("untrusted.invalid", html);
        Assert.DoesNotContain("private=value", html);
        Assert.Contains("/css/map-basemap.css", html);
        Assert.Contains("sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=", html);
        if (expected == "stadia") await Capture("home-stadia", html);
    }
    [Theory]
    [InlineData("stadia")]
    [InlineData("carto")]
    public async Task ConfiguredBasemapPreservesActiveHomeWalkPresentation(string provider)
    {
        app.Configuration["Basemap:Provider"] = provider;
        app.Configuration["Basemap:CartoApiKey"] = "fixture-public-key";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dog = new Dog { Name = "Test dog", OwnerId = "admin" };
            db.Dogs.Add(dog);
            db.Walks.Add(new Walk { Dog = dog, OwnerId = "admin", StartedAt = DateTime.UtcNow,
                Points = [new WalkPoint { Latitude = 46.56, Longitude = 15.64, RecordedAt = DateTime.UtcNow }] });
            await db.SaveChangesAsync();
        }
        using var client = Client("admin");
        var html = await Page(client, "/");
        Assert.Contains($"data-provider=\"{provider}\"", html);
        Assert.Contains("id=\"homeActiveWalkSheet\"", html);
        Assert.Contains("map-action-stack--with-active", html);
        Assert.DoesNotContain("id=\"homeIntro\"", html);
        await Capture(provider == "carto" ? "home-active-carto" : "home-active", html);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(" key", false)]
    [InlineData("key ", false)]
    [InlineData("key\r\nInjected", false)]
    [InlineData("<script>alert(1)</script>", false)]
    [InlineData("x\" onload=\"alert(1)", false)]
    [InlineData("a&UserId=123", false)]
    [InlineData("https://untrusted.invalid", false)]
    [InlineData("{z}", false)]
    [InlineData("č", false)]
    [InlineData("fixture-public-key_123.abc~", true)]
    public async Task CartoConfigurationOnlyEmitsAnExplicitValidBrowserKey(string? key, bool valid)
    {
        app.Configuration["Basemap:Provider"] = "carto";
        app.Configuration["Basemap:CartoApiKey"] = key;
        app.Configuration["OpenRouteService:ApiKey"] = "fixture-private-ors-must-not-leak";
        app.Configuration["Basemap:Url"] = "https://untrusted.invalid";
        using var client = Client(); var html = await Page(client, "/");
        Assert.Contains($"data-provider=\"{(valid ? "carto" : "osm")}\"", html);
        var emitted = Regex.Match(html, "data-carto-api-key=\"([^\"]*)\"").Groups[1].Value;
        Assert.Equal(valid ? key : "", WebUtility.HtmlDecode(emitted));
        Assert.DoesNotContain("fixture-private-ors-must-not-leak", html);
        Assert.DoesNotContain("untrusted.invalid", html);
        Assert.DoesNotContain("onload=\"alert", html);
        if (valid) await Capture("home-carto", html);
    }

    [Theory]
    [InlineData(null)] [InlineData("osm")] [InlineData("stadia")] [InlineData("CARTO")] [InlineData("carto ")]
    public async Task NonCartoProvidersNeverEmitConfiguredCartoKey(string? provider)
    {
        app.Configuration["Basemap:Provider"] = provider;
        app.Configuration["Basemap:CartoApiKey"] = "fixture-public-key-must-not-be-emitted";
        using var client = Client(); var html = await Page(client, "/");
        Assert.Contains($"data-provider=\"{(provider == "stadia" ? "stadia" : "osm")}\"", html);
        Assert.DoesNotContain("fixture-public-key-must-not-be-emitted", html);
    }

    [Theory] [InlineData(512, true)] [InlineData(513, false)]
    public async Task CartoKeyConfigurationHasABoundedLength(int length, bool valid)
    {
        app.Configuration["Basemap:Provider"] = "carto";
        app.Configuration["Basemap:CartoApiKey"] = new string('x', length);
        using var client = Client(); var html = await Page(client, "/");
        Assert.Contains($"data-provider=\"{(valid ? "carto" : "osm")}\"", html);
    }
    [Fact]
    public async Task SuccessfulQuickEditReloadsNewCoordinatesAndCategoryAtTheSamePlace()
    {
        using var client = Client("admin"); var edit = await Page(client, "/AdminPlaces/Edit/1?returnTo=map");
        Assert.Equal("map", Field(edit, "returnTo")); await Capture("edit-map", edit);
        var saved = await client.PostAsync("/AdminPlaces/Edit/1", Form(edit, ("returnTo", "map"), ("Latitude", "46.392"), ("Longitude", "15.575"), ("Category", "Veterinarian")));
        if (saved.StatusCode != HttpStatusCode.Redirect) await Capture("edit-unexpected", await saved.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode); Assert.Equal("/?placeId=1", saved.Headers.Location?.ToString());
        var home = await Page(client, saved.Headers.Location!.ToString()); using var places = Places(home);
        var row = places.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == 1);
        Assert.Equal(46.392, row.GetProperty("latitude").GetDouble()); Assert.Equal(15.575, row.GetProperty("longitude").GetDouble());
        Assert.Equal("veterinarian", row.GetProperty("categoryKey").GetString()); Assert.Equal("Veterinar", row.GetProperty("categoryLabel").GetString());
    }
    [Fact]
    public async Task DeactivationUsesVersionedEditAndReturnsWithoutInactiveFocus()
    {
        using var client = Client("admin"); var edit = await Page(client, "/AdminPlaces/Edit/1?returnTo=map");
        var saved = await client.PostAsync("/AdminPlaces/Edit/1", Form(edit, ("returnTo", "map"), ("isActive", "false")));
        Assert.Equal("/", saved.Headers.Location?.ToString());
        using var places = Places(await Page(client, "/?placeId=1"));
        Assert.DoesNotContain(places.RootElement.EnumerateArray(), p => p.GetProperty("id").GetInt32() == 1);
    }
    [Fact]
    public async Task ValidationRetainsInputAndSafeContextWithoutMutation()
    {
        using var client = Client("admin"); var edit = await Page(client, "/AdminPlaces/Edit/1?returnTo=map");
        var response = await client.PostAsync("/AdminPlaces/Edit/1", Form(edit, ("returnTo", "map"), ("Name", "<Unsaved & name>"), ("Latitude", "91"), ("isActive", "false")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var html = await response.Content.ReadAsStringAsync();
        Assert.Equal("map", Field(html, "returnTo")); Assert.Equal("<Unsaved & name>", Field(html, "Name"));
        Assert.Contains("field-validation-error", html); Assert.Matches("value=\"false\" selected", html); await Capture("edit-invalid", html);
        await using var scope = app.Services.CreateAsyncScope(); var p = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Places.FindAsync(1);
        Assert.Equal("Mr.Pet Test", p!.Name); Assert.Equal(46, p.Latitude); Assert.True(p.IsActive);
    }
    [Theory] [InlineData(null)] [InlineData("https://evil.example")] [InlineData("//evil.example")] [InlineData("/\\evil.example")] [InlineData("map?placeId=2")] [InlineData("/Map?placeId=1")]
    public async Task ArbitraryReturnTargetsAreIgnoredAndNormalEditRedirectStaysUnchanged(string? target)
    {
        using var client = Client("admin"); var edit = await Page(client, "/AdminPlaces/Edit/1?returnTo=" + Uri.EscapeDataString(target ?? ""));
        Assert.Equal("", Field(edit, "returnTo"));
        var response = await client.PostAsync("/AdminPlaces/Edit/1", Form(edit, ("returnTo", target ?? ""), ("returnUrl", "https://evil.example")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/AdminPlaces", response.Headers.Location?.ToString());
    }
    [Fact]
    public async Task StaleQuickEditCannotOverwriteOrDeactivateWinnerAndRefreshKeepsContext()
    {
        using var client = Client("admin"); var edit = await Page(client, "/AdminPlaces/Edit/1?returnTo=map");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var p = await db.Places.FindAsync(1);
            p!.Name = "Winner"; p.UpdatedAt = PlaceUpdates.NextUpdatedAt(p.UpdatedAt); await db.SaveChangesAsync();
        }
        var response = await client.PostAsync("/AdminPlaces/Edit/1", Form(edit, ("returnTo", "map"), ("isActive", "false")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/AdminPlaces/Edit/1?returnTo=map", html); Assert.DoesNotContain("class=\"places-form\"", html); await Capture("edit-conflict", html);
        await using var check = app.Services.CreateAsyncScope(); var winner = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().Places.FindAsync(1);
        Assert.Equal("Winner", winner!.Name); Assert.True(winner.IsActive);
    }
    [Fact]
    public async Task CreateDoesNotAdoptMapReturnContext()
    {
        using var client = Client("admin"); var form = await Page(client, "/AdminPlaces/Create");
        var response = await client.PostAsync("/AdminPlaces/Create", Form(form, ("Name", "New"), ("returnTo", "map")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Equal("/AdminPlaces", response.Headers.Location?.ToString());
    }
    [Theory] [InlineData("1")] [InlineData("2")] [InlineData("99999")] [InlineData("-1")] [InlineData("bad")]
    public async Task HomeFocusParameterDoesNotExposeInactiveOrMissingPlaces(string id)
    {
        using var client = Client(); using var places = Places(await Page(client, "/?placeId=" + id));
        Assert.Equal(new[] { 1, 4 }, places.RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetInt32()));
    }
    [Fact]
    public async Task EveryCategoryRendersTheSameApplicationOwnedIconInMapDiscoveryAndDetails()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var category in Enum.GetValues<PlaceCategory>())
                db.Places.Add(new Place { Id = 100 + (int)category, Name = category == PlaceCategory.DogPark ? "Pasje igrišče Vir" : "Icon fixture " + category,
                    Category = category, Latitude = 46.156105842, Longitude = 14.600444441 });
            await db.SaveChangesAsync();
        }
        using var client = Client();
        using var map = Places(await Page(client, "/"));
        var discovery = await Page(client, "/Places");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var category in Enum.GetValues<PlaceCategory>())
                db.SavedPlaces.Add(new SavedPlace { UserId = "user", PlaceId = 100 + (int)category });
            await db.SaveChangesAsync();
        }
        using var member = Client("user");
        var saved = await Page(member, "/SavedPlaces");
        Assert.Contains("/css/place-category-icons.css", discovery);
        Assert.Contains("/css/place-category-icons.css", saved);
        foreach (var category in Enum.GetValues<PlaceCategory>())
        {
            var presentation = PlaceCategories.Get(category);
            var row = map.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == 100 + (int)category);
            Assert.Equal(presentation.IconClass, row.GetProperty("iconClass").GetString());
            Assert.StartsWith("dd-place-icon--", presentation.IconClass);
            Assert.DoesNotContain("<svg", row.GetRawText());
            Assert.Contains($"dd-place-icon {presentation.IconClass}", discovery);
            Assert.Contains($"dd-place-icon {presentation.IconClass}", saved);
            var route = row.GetProperty("detailsUrl").GetString()!;
            var details = await Page(client, route);
            Assert.Contains($"data-icon-class=\"{presentation.IconClass}\"", details);
            Assert.Contains($"data-category-label=\"{presentation.Label}\"", WebUtility.HtmlDecode(details));
            await Capture("icons-details-" + presentation.Key, details);
        }
        await Capture("icons-discovery", discovery);
        await Capture("icons-saved", saved);
        var logoRoute = map.RootElement.EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == 1).GetProperty("detailsUrl").GetString()!;
        await Capture("icons-details-logo", await Page(client, logoRoute));
    }
    private static async Task Capture(string name, string html)
    {
        var path = Environment.GetEnvironmentVariable("DOGGYDROP_POPUP_CAPTURE");
        if (path != null) { Directory.CreateDirectory(path); await File.WriteAllTextAsync(Path.Combine(path, name + ".html"), html); }
    }
    public async Task DisposeAsync() { if (app != null) await app.DisposeAsync(); if (File.Exists(database)) File.Delete(database); }
    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (user is not ("admin" or "user")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)], IdentityConstants.ApplicationScheme);
            if (user == "admin") identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
