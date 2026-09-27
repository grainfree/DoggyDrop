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
public sealed class PlaceAmenitiesHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-amenity-http-{Guid.NewGuid():N}.db");
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
        builder.Services.AddSingleton<IPlaceLogoStorage, MissingPlaceLogoStorage>();
        builder.Services.AddScoped<IPlaceLogoReferenceReader, PlaceLogoReferenceReader>();
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
            db.Users.AddRange(new ApplicationUser { Id = "alice", UserName = "Alice" }, new ApplicationUser { Id = "bob", UserName = "Bob" }, new ApplicationUser { Id = "admin", UserName = "Admin" });
            foreach (var category in PlaceCategories.Supported)
                db.Places.Add(new Place { Name = $"Lokacija {category} z dolgim imenom", Category = category,
                    Address = "Daljši naslov 123, Ljubljana", Latitude = 46.05, Longitude = 14.51,
                    LogoUrl = $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{new string('a', 32)}.webp" });
            await db.SaveChangesAsync();
            var vet = await db.Places.SingleAsync(p => p.Category == PlaceCategory.Veterinarian);
            vet.AmenitiesSourceUrl = "https://internal.example/admin-only-source";
            vet.AmenitiesVerifiedAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
            foreach (var type in Enum.GetValues<PlaceAmenityType>()) vet.Amenities.Add(new PlaceAmenity { AmenityType = type });
            var cafe = await db.Places.SingleAsync(p => p.Category == PlaceCategory.DogFriendlyCafe);
            cafe.Amenities.Add(new PlaceAmenity { AmenityType = PlaceAmenityType.DogsTerrace });
            db.Places.Add(new Place { Name = "Inactive", Category = PlaceCategory.DogPark, IsActive = false });
            db.Places.Add(new Place { Name = "Unsupported", Category = (PlaceCategory)99 });
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

        return await response.Content.ReadAsStringAsync();
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static string MainContent(string html) => WebUtility.HtmlDecode(Regex.Match(html, "<main[\\s\\S]*?</main>").Value);

    [Fact]
    public async Task PublicDetailsRenderOnlyConfirmedFactsWithoutInternalMetadata()
    {
        using var anonymous = Client();
        var all = await Page(anonymous, "/Places/Details/1");
        Assert.Contains("<h2 id=\"placeAmenitiesTitle\">Za pse</h2>", all);
        foreach (var amenity in PlaceAmenities.All) Assert.Contains(amenity.Label, MainContent(all));
        Assert.DoesNotContain("admin-only-source", all);
        Assert.DoesNotContain("AmenitiesVerifiedAt", all);
        Assert.DoesNotContain("Preverjeno:", MainContent(all));
        Assert.DoesNotContain("niso dovoljeni", MainContent(all));
        var cafe = await Page(anonymous, "/Places/Details/5");
        Assert.Contains("Psi dobrodošli na terasi", MainContent(cafe));
        Assert.DoesNotContain("Psi dobrodošli v notranjosti", MainContent(cafe));
        foreach (var id in new[] { 6, 7 })
        {
            var unknown = await Page(anonymous, $"/Places/Details/{id}");
            Assert.DoesNotContain("place-amenities", unknown);
            Assert.DoesNotContain("Za pse", MainContent(unknown));
            foreach (var amenity in PlaceAmenities.All) Assert.DoesNotContain(amenity.Label, MainContent(unknown));
        }
        foreach (var id in new[] { 8, 9 })
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/Places/Details/{id}")).StatusCode);
        var discovery = await Page(anonymous, "/Places");
        Assert.DoesNotContain("place-amenities", discovery);
        Assert.DoesNotContain("admin-only-source", discovery);
        await Capture("amenities-details-all", all);
        await Capture("amenities-details-terrace", cafe);
        await Capture("amenities-details-empty", await Page(anonymous, "/Places/Details/6"));
    }

    [Fact]
    public async Task AdminCheckboxesArePreselectedAndPostsWorkWithoutJavaScriptOrOverposting()
    {
        using var admin = Client("admin");
        var page = await Page(admin, "/AdminPlaces/Edit/1");
        var checkboxes = Regex.Matches(page, "<input[^>]*name=\"AmenityTypes\"[^>]*>").Select(m => m.Value).ToArray();
        Assert.Equal(8, checkboxes.Length);
        Assert.All(checkboxes, box => Assert.Contains("checked=\"checked\"", box));
        Assert.Contains("Preverjeno: 25. 9. 2026 (UTC)", MainContent(page));
        Assert.Contains("admin-only-source", page);
        Assert.DoesNotContain("name=\"AmenitiesVerifiedAt\"", page);
        foreach (var group in new[] { "Dostop", "Na lokaciji", "Prostor" }) Assert.Contains($"<legend>{group}</legend>", page);
        await Capture("amenities-admin-edit", page);
        var create = await Page(admin, "/AdminPlaces/Create");
        Assert.DoesNotContain("checked=\"checked\"", string.Join("", Regex.Matches(create, "<input[^>]*name=\"AmenityTypes\"[^>]*>").Select(m => m.Value)));
        Assert.Contains("Ni podatka o preverjanju", MainContent(create));
        await Capture("amenities-admin-create", create);

        var data = Form(Token(create), Enumerable.Range(1, 8).Select(i => i.ToString()).Append("3"));
        data.Add(new("VerifyAmenitiesToday", "true"));
        data.Add(new("AmenitiesVerifiedAt", "2099-01-01T00:00:00Z"));
        data.Add(new("PlaceId", "1"));
        data.Add(new("UserId", "bob"));
        data.Add(new("Amenities[0].PlaceId", "1"));
        var before = DateTime.UtcNow;
        var response = await admin.PostAsync("/AdminPlaces/Create", new FormUrlEncodedContent(data));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            WebUtility.HtmlDecode(string.Join("\n", Regex.Matches(await response.Content.ReadAsStringAsync(), "<span[^>]*field-validation-error[^>]*>[\\s\\S]*?</span>").Select(m => m.Value))));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var place = await db.Places.AsNoTracking().Include(p => p.Amenities).SingleAsync(p => p.Name == "New amenities test");
        Assert.NotEqual(1, place.Id);
        Assert.Equal(8, place.Amenities.Count);
        Assert.InRange(place.AmenitiesVerifiedAt!.Value, before, DateTime.UtcNow);
        Assert.Equal(8, await db.PlaceAmenities.CountAsync(a => a.PlaceId == 1));

        var editPage = await Page(admin, $"/AdminPlaces/Edit/{place.Id}");
        var editForm = Form(Token(editPage), ["2", "3"]);
        editForm.Add(new("OriginalUpdatedAt", Version(editPage)));
        editForm.RemoveAll(pair => pair.Key == "Category"); editForm.Add(new("Category", "5"));
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync($"/AdminPlaces/Edit/{place.Id}", new FormUrlEncodedContent(editForm))).StatusCode);
        var updated = await db.Places.AsNoTracking().Include(p => p.Amenities).SingleAsync(p => p.Id == place.Id);
        Assert.Equal(PlaceCategory.DogFriendlyCafe, updated.Category);
        Assert.Equal([PlaceAmenityType.DogsTerrace, PlaceAmenityType.WaterForDogs], updated.Amenities.OrderBy(a => a.AmenityType).Select(a => a.AmenityType));
        Assert.Null(updated.AmenitiesVerifiedAt);
        var clearPage = await Page(admin, $"/AdminPlaces/Edit/{place.Id}");
        var clear = Form(Token(clearPage), []);
        clear.Add(new("OriginalUpdatedAt", Version(clearPage)));
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync($"/AdminPlaces/Edit/{place.Id}", new FormUrlEncodedContent(clear))).StatusCode);
        Assert.Empty(await db.PlaceAmenities.Where(a => a.PlaceId == place.Id).ToListAsync());
    }

    [Fact]
    public async Task OrdinaryUsersCannotEditAndAdminMustSubmitAntiforgeryAndValidValues()
    {
        using var anonymous = Client();
        using var user = Client("alice");
        using var admin = Client("admin");
        foreach (var endpoint in new[] { "/AdminPlaces/Create", "/AdminPlaces/Edit/1" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(endpoint)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(endpoint)).StatusCode);
            var userToken = Token(await Page(user, "/Places"));
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync(endpoint, new FormUrlEncodedContent(Form(userToken, ["1"])))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(endpoint, new FormUrlEncodedContent(Form("", ["1"])))).StatusCode);
        }
        var token = Token(await Page(admin, "/AdminPlaces/Create"));
        foreach (var value in new[] { "0", "9", "-1", "not-an-enum" })
        {
            var response = await admin.PostAsync("/AdminPlaces/Create", new FormUrlEncodedContent(Form(token, [value])));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // redisplayed validation, no redirect
            Assert.Contains("field-validation-error", await response.Content.ReadAsStringAsync());
        }
        var unsafeSource = Form(token, ["1"]);
        unsafeSource.RemoveAll(pair => pair.Key == "AmenitiesSourceUrl");
        unsafeSource.Add(new("AmenitiesSourceUrl", "javascript:alert(1)"));
        var invalid = await admin.PostAsync("/AdminPlaces/Create", new FormUrlEncodedContent(unsafeSource));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("field-validation-error", await invalid.Content.ReadAsStringAsync());
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(9, await db.Places.CountAsync());
        Assert.Equal(9, await db.PlaceAmenities.CountAsync());
    }

    private static string Version(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"OriginalUpdatedAt\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static List<KeyValuePair<string, string>> Form(string token, IEnumerable<string> amenities)
    {
        List<KeyValuePair<string, string>> fields = [new("Name", "New amenities test"), new("Category", "6"),
            new("Latitude", "46"), new("Longitude", "15"), new("AmenitiesSourceUrl", "https://example.com/dogs"),
            new("__RequestVerificationToken", token)];
        fields.AddRange(amenities.Select(type => new KeyValuePair<string, string>("AmenityTypes", type)));
        return fields;
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
            if (user is not ("alice" or "bob" or "admin")) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user), new Claim(ClaimTypes.Name, user)
            ], IdentityConstants.ApplicationScheme));
            if (user == "admin") ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
