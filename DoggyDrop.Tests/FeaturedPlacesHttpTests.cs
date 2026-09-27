using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
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
public sealed class FeaturedPlacesHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-amenity-http-{Guid.NewGuid():N}.db");
    private readonly FeaturedPlacesTests.Clock clock = new();
    private WebApplication app = null!;
    private readonly TestLogos logos = new();

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
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<IPlaceLogoStorage>(logos);
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
            vet.IsFeatured = true; vet.FeaturedUntil = FeaturedPlacesTests.Now.AddSeconds(1);
            vet.AmenitiesSourceUrl = "https://internal.example/admin-only-source";
            vet.AmenitiesVerifiedAt = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc);
            foreach (var type in Enum.GetValues<PlaceAmenityType>()) vet.Amenities.Add(new PlaceAmenity { AmenityType = type });
            var shop = await db.Places.SingleAsync(p => p.Category == PlaceCategory.PetShop);
            shop.IsFeatured = true; shop.FeaturedFrom = FeaturedPlacesTests.Now.AddDays(1);
            var groomer = await db.Places.SingleAsync(p => p.Category == PlaceCategory.Groomer);
            groomer.IsFeatured = true; groomer.FeaturedUntil = FeaturedPlacesTests.Now;
            var park = await db.Places.SingleAsync(p => p.Category == PlaceCategory.DogPark);
            park.IsFeatured = true;
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
    public async Task RenderedPublicDisclosureExpiresAndNeverLeaksScheduling()
    {
        using var anonymous=Client();
        var discovery=await Page(anonymous,"/Places");
        Assert.Single(Regex.Matches(discovery,"class=\"place-featured-badge\""));
        var response=await anonymous.GetAsync("/Places");Assert.True(response.Headers.CacheControl!.NoStore);
        var details=await Page(anonymous,"/Places/Details/1");Assert.Contains("Izpostavljeno",MainContent(details));
        Assert.Contains("Navodila za pot",MainContent(details));Assert.Contains("Za pse",MainContent(details));
        foreach(var html in new[]{discovery,details}) {
            Assert.DoesNotContain("FeaturedFrom",html);Assert.DoesNotContain("FeaturedUntil",html);
            Assert.DoesNotContain("admin-only-source",html);Assert.DoesNotContain("Priporočamo",MainContent(html));
        }
        foreach(var id in new[]{2,3,6,7})Assert.DoesNotContain("place-featured-badge",await Page(anonymous,$"/Places/Details/{id}"));
        await Capture("featured-discovery",discovery);await Capture("featured-details",details);
        await Capture("normal-details",await Page(anonymous,"/Places/Details/2"));
        clock.Value=FeaturedPlacesTests.Now.AddSeconds(1);
        Assert.DoesNotContain("place-featured-badge",await Page(anonymous,"/Places/Details/1"));
        Assert.DoesNotContain("place-featured-badge",await Page(anonymous,"/Places"));
    }
    [Fact]
    public async Task AdminFormsPersistLocalScheduleAndIgnoreInternalOverposting()
    {
        using var admin=Client("admin");var page=await Page(admin,"/AdminPlaces/Edit/1");
        Assert.Contains("Izpostavitev",MainContent(page));Assert.Contains("Europe/Ljubljana",page);
        Assert.Contains("2026-09-27T14:00:01",page);Assert.DoesNotContain("name=\"FeaturedUntil\"",page);
        await Capture("featured-admin-edit",page);
        var create=await Page(admin,"/AdminPlaces/Create");await Capture("featured-admin-create",create);
        var fields=Form(Token(create));fields.Add(new("IsFeatured","true"));fields.Add(new("FeaturedFromLocal","2026-09-27T14:00"));
        fields.Add(new("FeaturedUntilLocal","2026-10-01T14:00"));fields.Add(new("FeaturedUntil","2099-01-01T00:00:00Z"));
        fields.Add(new("IsActive","false"));fields.Add(new("AmenitiesVerifiedAt","2099-01-01T00:00:00Z"));fields.Add(new("Id","1"));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminPlaces/Create",new FormUrlEncodedContent(fields))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var place=await db.Places.SingleAsync(p=>p.Name=="Featured test");Assert.NotEqual(1,place.Id);Assert.True(place.IsActive);Assert.True(place.IsFeatured);
        Assert.Equal(FeaturedPlacesTests.Now,place.FeaturedFrom);Assert.Equal(new DateTime(2026,10,1,12,0,0,DateTimeKind.Utc),place.FeaturedUntil);Assert.Null(place.AmenitiesVerifiedAt);
        var edit=EditForm(await Page(admin,$"/AdminPlaces/Edit/{place.Id}"));edit.RemoveAll(p=>p.Key=="Category");edit.Add(new("Category","6"));Put(edit,"IsFeatured","false");
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaces/Edit/{place.Id}",new FormUrlEncodedContent(edit))).StatusCode);
        await db.Entry(place).ReloadAsync();Assert.False(place.IsFeatured);Assert.Null(place.FeaturedFrom);Assert.Null(place.FeaturedUntil);
    }
    [Theory]
    [InlineData("6","true","","")]
    [InlineData("7","true","","")]
    [InlineData("1","true","2026-09-27T14:00","2026-09-27T14:00")]
    [InlineData("1","true","2026-10-25T02:30","")]
    [InlineData("1","true","","2026-03-29T02:30")]
    public async Task InvalidFeaturedPostsReturnUsefulValidationWithoutSaving(string category,string featured,string from,string until)
    {
        using var admin=Client("admin");var fields=Form(Token(await Page(admin,"/AdminPlaces/Create")));
        fields.RemoveAll(p=>p.Key=="Category");fields.Add(new("Category",category));fields.Add(new("IsFeatured",featured));
        fields.Add(new("FeaturedFromLocal",from));fields.Add(new("FeaturedUntilLocal",until));
        var response=await admin.PostAsync("/AdminPlaces/Create",new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("field-validation-error",await response.Content.ReadAsStringAsync());
        await using var scope=app.Services.CreateAsyncScope();Assert.Equal(9,await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Places.CountAsync());
    }
    [Fact]
    public async Task FeaturedMutationRequiresAdminAndAntiforgery()
    {
        using var anon=Client();using var user=Client("alice");using var admin=Client("admin");
        foreach(var endpoint in new[]{"/AdminPlaces/Create","/AdminPlaces/Edit/1"}) {
            var fields=Form("");fields.Add(new("IsFeatured","true"));
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync(endpoint,new FormUrlEncodedContent(fields))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync(endpoint,new FormUrlEncodedContent(fields))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync(endpoint,new FormUrlEncodedContent(fields))).StatusCode);
        }
    }
    private static List<KeyValuePair<string,string>> Form(string token) => [new("Name","Featured test"),new("Category","1"),new("Latitude","46"),new("Longitude","15"),new("__RequestVerificationToken",token)];

    private const string ConflictMessage = "Lokacija je bila med urejanjem spremenjena. Osveži podatke in poskusi znova.";
    private static string Value(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"" + name + "\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static List<KeyValuePair<string,string>> EditForm(string html)
    {
        var fields = Form(Token(html));
        Put(fields, "Name", Value(html, "Name"));
        foreach (var name in new[] { "OriginalUpdatedAt", "FeaturedFromLocal", "FeaturedUntilLocal", "AmenitiesSourceUrl" })
            fields.Add(new(name, Value(html, name)));
        foreach (Match input in Regex.Matches(html, "<input[^>]+>"))
            if (input.Value.Contains("checked=\"checked\""))
                foreach (var name in new[] { "IsFeatured", "AmenityTypes" })
                    if (input.Value.Contains($"name=\"{name}\"")) fields.Add(new(name, Value(input.Value, name)));
        return fields;
    }
    private static void Put(List<KeyValuePair<string,string>> fields, string name, string value)
    { fields.RemoveAll(p => p.Key == name); fields.Add(new(name, value)); }
    private async Task<Place> Stored()
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Places.AsNoTracking()
            .Include(p => p.Amenities).SingleAsync(p => p.Id == 1);
    }
    private static string Facts(Place p) => System.Text.Json.JsonSerializer.Serialize(new {
        p.Name, p.Category, p.Latitude, p.Longitude, p.Phone, p.Address, p.IsActive, p.DataSourceId,
        p.IsFeatured, p.FeaturedFrom, p.FeaturedUntil, p.AmenitiesSourceUrl, p.AmenitiesVerifiedAt,
        p.UpdatedAt, p.LogoUrl, Amenities = p.Amenities.Select(a => a.AmenityType).Order().ToArray()
    });
    private static async Task AssertConflict(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        await Capture("conflict-edit", html);
        Assert.Contains(ConflictMessage, html);
        Assert.Contains("href=\"/AdminPlaces/Edit/1\"", html);
        Assert.DoesNotContain("name=\"OriginalUpdatedAt\"", html);
        Assert.DoesNotContain("Shrani spremembe", html);
    }

    [Fact]
    public async Task OriginalTokenRoundTripsAndOrdinaryEditSucceeds()
    {
        using var admin = Client("admin");
        var original = await Stored();
        var page = await Page(admin, "/AdminPlaces/Edit/1");
        var fields = EditForm(page);
        var input = new PlaceInput { OriginalUpdatedAt = Value(page, "OriginalUpdatedAt") };
        Assert.True(input.TryOriginalUpdatedAt(out var parsed));
        Assert.Equal(original.UpdatedAt.Ticks, parsed.Ticks);
        Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        Assert.DoesNotContain("name=\"OriginalUpdatedAt\"", await Page(admin, "/AdminPlaces/Create"));
        Put(fields, "Name", "Ordinary current form edit");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(fields))).StatusCode);
        var saved = await Stored();
        Assert.Equal("Ordinary current form edit", saved.Name);
        Assert.True(saved.UpdatedAt > original.UpdatedAt);
        Assert.Equal(original.AmenitiesVerifiedAt, saved.AmenitiesVerifiedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken")]
    [InlineData("2026-09-27T12:00:00.0000000")]
    [InlineData("2026-09-27T14:00:00.0000000+02:00")]
    [InlineData("2099-01-01T00:00:00.0000000Z")]
    public async Task MissingMalformedOrWrongTokenCannotSave(string? token)
    {
        using var admin = Client("admin");
        var fields = EditForm(await Page(admin, "/AdminPlaces/Edit/1"));
        var before = Facts(await Stored());
        fields.RemoveAll(p => p.Key == "OriginalUpdatedAt");
        if (token != null) fields.Add(new("OriginalUpdatedAt", token));
        Put(fields, "Name", "Must not save");
        await AssertConflict(await admin.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(fields)));
        Assert.Equal(before, Facts(await Stored()));
    }

    [Theory]
    [InlineData("Featured")]
    [InlineData("Amenities")]
    [InlineData("Unrelated")]
    [InlineData("Combined")]
    public async Task TwoOpenFormsCannotOverwriteANewerSuccessfulEdit(string change)
    {
        using var a = Client("admin"); using var b = Client("admin");
        var first = EditForm(await Page(a, "/AdminPlaces/Edit/1"));
        var stale = EditForm(await Page(b, "/AdminPlaces/Edit/1"));
        Assert.Equal(first.Single(p => p.Key == "OriginalUpdatedAt").Value, stale.Single(p => p.Key == "OriginalUpdatedAt").Value);
        if (change is "Featured" or "Combined") Put(first, "FeaturedUntilLocal", "2026-12-01T12:00");
        if (change is "Amenities" or "Combined") {
            Put(first, "AmenityTypes", "3"); Put(first, "AmenitiesSourceUrl", "https://example.com/winner");
            Put(first, "VerifyAmenitiesToday", "true");
        }
        if (change is "Unrelated" or "Combined") Put(first, "Name", "Winning name");
        Assert.Equal(HttpStatusCode.Redirect, (await a.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(first))).StatusCode);
        var winner = await Stored();
        if (change is "Featured" or "Combined") Assert.Equal(new DateTime(2026,12,1,11,0,0), winner.FeaturedUntil);
        if (change is "Amenities" or "Combined") {
            Assert.Equal(PlaceAmenityType.WaterForDogs, Assert.Single(winner.Amenities).AmenityType);
            Assert.Equal("https://example.com/winner", winner.AmenitiesSourceUrl); Assert.NotNull(winner.AmenitiesVerifiedAt);
        }
        if (change is "Unrelated" or "Combined") Assert.Equal("Winning name", winner.Name);
        Put(stale, "Name", "Stale name"); Put(stale, "Category", "2"); Put(stale, "Phone", "123");
        Put(stale, "AmenityTypes", "4"); Put(stale, "AmenitiesSourceUrl", "https://example.com/stale");
        await AssertConflict(await b.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(stale)));
        Assert.Equal(Facts(winner), Facts(await Stored()));
        // Deliberate reload, not conflict redisplay, obtains a usable current version.
        var refreshed = EditForm(await Page(b, "/AdminPlaces/Edit/1"));
        Put(refreshed, "Phone", "456");
        Assert.Equal(HttpStatusCode.Redirect, (await b.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(refreshed))).StatusCode);
        Assert.Equal("456", (await Stored()).Phone);
    }

    [Theory]
    [InlineData("AssignSource")]
    [InlineData("ClearSource")]
    [InlineData("Activate")]
    [InlineData("Deactivate")]
    [InlineData("SetActive")]
    public async Task BulkAndActivationInvalidateAlreadyOpenEdit(string action)
    {
        using var admin = Client("admin");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = new DataSource { Name = "Local test source" }; db.DataSources.Add(source);
        var place = await db.Places.SingleAsync(p => p.Id == 1);
        place.IsActive = action != "Activate";
        if (action == "ClearSource") place.DataSource = source;
        await db.SaveChangesAsync();
        var fields = EditForm(await Page(admin, "/AdminPlaces/Edit/1"));
        if (action == "SetActive")
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/AdminPlaces/SetActive/1", new FormUrlEncodedContent([
                new("__RequestVerificationToken", fields.Single(p => p.Key == "__RequestVerificationToken").Value), new("isActive", "false")]))).StatusCode);
        else
            Assert.Equal(1, await new AdminBulkTools(db).ApplyAsync(new BulkInput {
                Target = BulkTarget.Places, Action = Enum.Parse<BulkAction>(action), Ids = [1],
                DataSourceId = action == "AssignSource" ? source.Id : null
            }));
        var winner = Facts(await Stored());
        await AssertConflict(await admin.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(fields)));
        Assert.Equal(winner, Facts(await Stored()));
    }

    [Fact]
    public async Task ValidationRedisplayPreservesOriginalVersionAndCannotRefreshStaleInput()
    {
        using var a = Client("admin"); using var b = Client("admin");
        var first = EditForm(await Page(a, "/AdminPlaces/Edit/1"));
        var old = EditForm(await Page(b, "/AdminPlaces/Edit/1"));
        var version = old.Single(p => p.Key == "OriginalUpdatedAt").Value;
        Put(old, "Name", "");
        var invalid = await b.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(old));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("field-validation-error", html);
        Assert.Equal(version, Value(html, "OriginalUpdatedAt"));
        Put(first, "Name", "Winner after validation");
        Assert.Equal(HttpStatusCode.Redirect, (await a.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(first))).StatusCode);
        // Even submitting invalid data after the winner cannot refresh the old token.
        await AssertConflict(await b.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(old)));
        var corrected = EditForm(html); Put(corrected, "Name", "Corrected but stale");
        await AssertConflict(await b.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(corrected)));
        Assert.Equal("Winner after validation", (await Stored()).Name);
    }

    private static MultipartFormDataContent WithLogo(List<KeyValuePair<string,string>> fields)
    {
        var form = new MultipartFormDataContent();
        foreach (var field in fields) form.Add(new StringContent(field.Value), field.Key);
        var file = new ByteArrayContent([137,80,78,71,13,10,26,10,0,0,0,0]);
        file.Headers.ContentType = new("image/png"); form.Add(file, "LogoFile", "logo.png");
        return form;
    }
    [Fact]
    public async Task AlreadyStaleLogoEditDoesNotCallStorage()
    {
        using var a = Client("admin"); using var b = Client("admin");
        var first = EditForm(await Page(a, "/AdminPlaces/Edit/1"));
        var old = EditForm(await Page(b, "/AdminPlaces/Edit/1"));
        Put(first, "Name", "Winner");
        Assert.Equal(HttpStatusCode.Redirect, (await a.PostAsync("/AdminPlaces/Edit/1", new FormUrlEncodedContent(first))).StatusCode);
        var winner = Facts(await Stored());
        using var form = WithLogo(old);
        await AssertConflict(await b.PostAsync("/AdminPlaces/Edit/1", form));
        Assert.Equal(0, logos.Uploads); Assert.Empty(logos.Deleted); Assert.Equal(winner, Facts(await Stored()));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RaceDuringLogoUploadRollsBackEverythingAndCleansOnlyUnreferencedUpload(bool winnerReferencesUpload)
    {
        using var admin = Client("admin");
        var fields = EditForm(await Page(admin, "/AdminPlaces/Edit/1"));
        Put(fields, "Category", "2"); Put(fields, "Name", "Losing name");
        Put(fields, "FeaturedUntilLocal", "2027-01-01T12:00"); Put(fields, "AmenityTypes", "4");
        Put(fields, "AmenitiesSourceUrl", "https://example.com/loser"); Put(fields, "VerifyAmenitiesToday", "true");
        string? winner = null;
        logos.OnUpload = async () => {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var place = await db.Places.Include(p => p.Amenities).SingleAsync(p => p.Id == 1);
            place.Name = "Winning during upload"; place.LogoUrl = TestLogos.Url(winnerReferencesUpload ? 'b' : 'c');
            place.UpdatedAt = PlaceUpdates.NextUpdatedAt(place.UpdatedAt);
            await db.SaveChangesAsync(); winner = Facts(await Stored());
        };
        using var form = WithLogo(fields);
        await AssertConflict(await admin.PostAsync("/AdminPlaces/Edit/1", form));
        Assert.Equal(1, logos.Uploads); Assert.NotNull(winner); Assert.Equal(winner, Facts(await Stored()));
        Assert.DoesNotContain(TestLogos.Url('c'), logos.Deleted);
        Assert.Equal(winnerReferencesUpload ? 0 : 1, logos.Deleted.Count);
        if (!winnerReferencesUpload) Assert.Equal(TestLogos.Url('b'), logos.Deleted.Single());
    }
    private sealed class TestLogos : IPlaceLogoStorage
    {
        public int Uploads; public List<string?> Deleted { get; } = [];
        public Func<Task>? OnUpload;
        public static string Url(char c) => $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{new string(c,32)}.webp";
        public async Task<string?> UploadAsync(IFormFile file) { Uploads++; if (OnUpload != null) await OnUpload(); return Url('b'); }
        public Task DeleteManagedAsync(string? url) { Deleted.Add(url); return Task.CompletedTask; }
    }

    // Opt-in rendered HTML snapshots for offline viewport checks, using only test data.
    private static async Task Capture(string name, string html)
    {
        var output = Environment.GetEnvironmentVariable("DOGGYDROP_FEATURED_REVIEW_OUTPUT");
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
