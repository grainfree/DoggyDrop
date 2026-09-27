using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// An isolated MVC host: never invokes DoggyDrop's Program/startup or runs migrations.
public sealed class SavedPlacesHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-saved-http-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop", "DoggyDrop.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PlacesController).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root.FullName, "DoggyDrop"), EnvironmentName = "Testing", Args = []
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Test";
            options.DefaultChallengeScheme = "Test";
        }).AddScheme<AuthenticationSchemeOptions, TestUserHandler>("Test", _ => { });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PlacesController).Assembly);
        builder.Services.AddRazorPages().AddApplicationPart(typeof(PlacesController).Assembly);
        app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}");
        app.MapRazorPages();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = "alice", UserName = "Alice" }, new ApplicationUser { Id = "bob", UserName = "Bob" });
            foreach (var category in PlaceCategories.Supported)
                db.Places.Add(new Place { Name = $"Lokacija {category} z dolgim imenom", Category = category,
                    Address = "Daljši naslov 123, Ljubljana", Latitude = 46.05, Longitude = 14.51,
                    LogoUrl = $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{new string('a', 32)}.webp" });
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

    private static async Task<string> Page(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await response.Content.ReadAsStringAsync();
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static string MainContent(string html) => WebUtility.HtmlDecode(Regex.Match(html, "<main[\\s\\S]*?</main>").Value);

    [Fact]
    public async Task PublicPagesExplainLoginAndPrivateEndpointsEnforceAuthAndAntiforgery()
    {
        using var anonymous = Client();
        var discovery = await Page(anonymous, "/Places");
        Assert.Equal(7, Regex.Matches(discovery, "<article class=\"place-discovery-card\"").Count);
        Assert.Contains("Za shranjevanje lokacij se prijavi.", MainContent(discovery));
        Assert.Contains("/Identity/Account/Login?returnUrl=%2FPlaces", discovery, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("action=\"/SavedPlaces/Save\"", discovery);
        var details = await Page(anonymous, "/Places/Details/1");
        Assert.Contains("/Identity/Account/Login?returnUrl=%2FPlaces%2FDetails%2F1", details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Navodila za pot", MainContent(details));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/SavedPlaces")).StatusCode);
        foreach (var action in new[] { "Save", "Remove" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/SavedPlaces/{action}", new FormUrlEncodedContent(new Dictionary<string, string> { ["placeId"] = "1" }))).StatusCode);
            using var signedIn = Client("alice");
            Assert.Equal(HttpStatusCode.BadRequest, (await signedIn.PostAsync($"/SavedPlaces/{action}", new FormUrlEncodedContent(new Dictionary<string, string> { ["placeId"] = "1" }))).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await signedIn.GetAsync($"/SavedPlaces/{action}")).StatusCode);
        }
        await Capture("anonymous-discovery", discovery);
        await Capture("anonymous-details", details);
    }

    [Fact]
    public async Task RenderedFormsSaveRemoveWithPrgAndIgnoreOverpostedUserId()
    {
        using var alice = Client("alice");
        using var bob = Client("bob");
        var discovery = await Page(alice, "/Places");
        var token = Token(discovery);
        Assert.NotEmpty(token);
        Assert.Contains("action=\"/SavedPlaces/Save\"", discovery);
        var empty = await Page(alice, "/SavedPlaces");
        Assert.Contains("Še nimaš shranjenih lokacij.", MainContent(empty));
        Assert.Contains("Razišči lokacije", MainContent(empty));
        var post = await alice.PostAsync("/SavedPlaces/Save", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["placeId"] = "1", ["UserId"] = "bob", ["returnUrl"] = "/Places/Details/1", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/Places/Details/1", post.Headers.Location?.OriginalString);
        var details = await Page(alice, post.Headers.Location!.OriginalString);
        Assert.Contains("Lokacija je shranjena.", MainContent(details));
        Assert.Contains("Shranjeno", MainContent(details));
        Assert.Contains("Odstrani", MainContent(details));
        Assert.Contains("action=\"/SavedPlaces/Remove\"", details);
        Assert.Contains("bi-bookmark-fill", details);
        Assert.DoesNotContain("action=\"/SavedPlaces/Remove\"", await Page(bob, "/Places/Details/1"));
        Assert.Contains("Še nimaš shranjenih lokacij.", MainContent(await Page(bob, "/SavedPlaces")));
        Assert.Contains("action=\"/SavedPlaces/Remove\"", await Page(alice, "/Places"));

        // Populate a full seven-category page for the optional local layout review artifact.
        for (var id = 2; id <= 7; id++)
            Assert.Equal(HttpStatusCode.Redirect, (await alice.PostAsync("/SavedPlaces/Save", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["placeId"] = id.ToString(), ["returnUrl"] = "/SavedPlaces", ["__RequestVerificationToken"] = token }))).StatusCode);
        var saved = await Page(alice, "/SavedPlaces");
        Assert.Equal(7, Regex.Matches(saved, "<article class=\"place-discovery-card\"").Count);
        Assert.DoesNotContain("discoveryLocate", saved);
        await Capture("signed-discovery", discovery);
        await Capture("saved", saved);
        await Capture("saved-details", details);
        await Capture("empty", empty);

        foreach (var id in Enumerable.Range(1, 7).Append(1))
        {
            var remove = await alice.PostAsync("/SavedPlaces/Remove", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["placeId"] = id.ToString(), ["UserId"] = "bob", ["returnUrl"] = "https://evil.example", ["__RequestVerificationToken"] = token }));
            Assert.Equal(HttpStatusCode.Redirect, remove.StatusCode);
            Assert.Equal("/SavedPlaces", remove.Headers.Location?.OriginalString);
        }
        Assert.Contains("Še nimaš shranjenih lokacij.", MainContent(await Page(alice, "/SavedPlaces")));
    }

    // Opt-in rendered HTML snapshots for offline viewport checks, using only test data.
    private static async Task Capture(string name, string html)
    {
        var output = Environment.GetEnvironmentVariable("DOGGYDROP_REVIEW_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, $"{name}.html"), html);
    }

    public async Task DisposeAsync()
    {
        if (app != null) await app.DisposeAsync();
        if (File.Exists(database)) File.Delete(database);
    }

    private sealed class TestUserHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers["X-Test-User"].ToString();
            if (user is not ("alice" or "bob")) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)
            ], IdentityConstants.ApplicationScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
