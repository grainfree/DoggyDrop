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
public sealed class DataProvenanceHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-provenance-http-{Guid.NewGuid():N}.db");
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
            db.DataSources.AddRange(new DataSource { Name = "Občina s podatki", Type = DataSourceType.Municipality,
                ContactName = "INTERNAL-CONTACT", ContactEmail = "private-contact@example.com", Notes = "INTERNAL-NOTES", WebsiteUrl = "https://internal-source.example/" },
                new DataSource { Name = "Drug vir", Type = DataSourceType.Partner });
            await db.SaveChangesAsync();
            for (var i = 1; i <= 2; i++) {
                db.Places.Add(new Place { Name = "Pasji park", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 + i * .00001,
                    DataSourceId = 1, AmenitiesVerifiedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                    Amenities = [new PlaceAmenity { AmenityType = PlaceAmenityType.Fenced }] });
                db.TrashBins.Add(new TrashBin { Name = $"Koš {i}", Latitude = 46, Longitude = 15 + i * .00001, DataSourceId = 1, IsApproved = i == 1 });
            }
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


    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html,
        $"name=\"{name}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) =>
        new(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)));

    [Fact]
    public async Task NewAdminPagesAndMutationsEnforceRoleAndAntiforgery()
    {
        using var anon = Client(); using var user = Client("alice"); using var admin = Client("admin");
        foreach (var path in new[] { "/AdminDataSources", "/AdminDataSources/Create", "/AdminDataSources/Edit/1", "/AdminDataSources/Delete/1", "/AdminBins", "/AdminDuplicates?target=Bins", "/AdminDuplicates?target=Places" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(path)).StatusCode);
        }
        var userToken = Token(await Page(user, "/Places"));
        foreach (var path in new[] { "/AdminDataSources/Create", "/AdminDataSources/Edit/1", "/AdminDataSources/Delete/1", "/AdminBulk/Preview", "/AdminBulk/Apply" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync(path, Form(("__RequestVerificationToken", userToken)))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(path, Form())).StatusCode);
        }
    }

    [Fact]
    public async Task SourceFormsValidateInputIgnoreOverpostingAndKeepDataDateIndependent()
    {
        using var admin = Client("admin"); var token = Token(await Page(admin, "/AdminDataSources/Create"));
        foreach (var invalid in new[] { "javascript:alert(1)", "data:text/html,test", "file:///bad", "/relative" })
        {
            var response = await admin.PostAsync("/AdminDataSources/Create", Form(("__RequestVerificationToken",token),("Name","Bad"),("Type","2"),("WebsiteUrl",invalid)));
            Assert.Equal(HttpStatusCode.OK,response.StatusCode); Assert.Contains("validation-summary-errors",await response.Content.ReadAsStringAsync());
        }
        foreach (var type in new[] { "0", "-1", "99", "bad" })
            Assert.Equal(HttpStatusCode.OK,(await admin.PostAsync("/AdminDataSources/Create",Form(("__RequestVerificationToken",token),("Name","Bad"),("Type",type)))).StatusCode);
        var created = await admin.PostAsync("/AdminDataSources/Create",Form(("__RequestVerificationToken",token),("Name","New origin"),("Type","6"),
            ("DataDate","2026-08-31"),("Id","1"),("CreatedAt","2099-01-01"),("UserId","alice")));
        Assert.Equal(HttpStatusCode.Redirect,created.StatusCode);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await db.DataSources.AsNoTracking().SingleAsync(s=>s.Name=="New origin");
        Assert.NotEqual(1,source.Id); Assert.True(source.CreatedAt.Year<2099); Assert.Equal(new DateOnly(2026,8,31),source.DataDate);
        var edit = await admin.PostAsync($"/AdminDataSources/Edit/{source.Id}",Form(("__RequestVerificationToken",token),("Name","New name"),("Type","6"),("DataDate","2026-08-31")));
        Assert.Equal(HttpStatusCode.Redirect,edit.StatusCode);
        Assert.Equal(source.DataDate,(await db.DataSources.AsNoTracking().SingleAsync(s=>s.Id==source.Id)).DataDate);
        Assert.Equal(3,await db.DataSources.CountAsync());
    }

    [Fact]
    public async Task BulkRequiresProtectedConfirmationRevalidatesIdsAndDoesNotTrustPostedAction()
    {
        using var admin = Client("admin"); var list = await Page(admin,"/AdminPlaces"); var token = Token(list);
        var preview = await admin.PostAsync("/AdminBulk/Preview",Form(("__RequestVerificationToken",token),("Target","Places"),("Action","AssignSource"),
            ("DataSourceId","2"),("Ids","1"),("Ids","2"),("Ids","1")));
        Assert.Equal(HttpStatusCode.OK,preview.StatusCode); var html = await preview.Content.ReadAsStringAsync();
        Assert.Contains("Izbranih: <strong>2</strong>",html); Assert.Contains("Drug vir",html);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>Assert.Equal(1,p.DataSourceId));
        var confirmation = Field(html,"token"); Assert.NotEmpty(confirmation);
        await Capture("bulk-confirm",html);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBulk/Apply",Form(("__RequestVerificationToken",token),("token",confirmation+"bad")))).StatusCode);
        var applied = await admin.PostAsync("/AdminBulk/Apply",Form(("__RequestVerificationToken",token),("token",confirmation),("Action","Deactivate"),("Ids","999")));
        Assert.Equal(HttpStatusCode.Redirect,applied.StatusCode);
        Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>{Assert.Equal(2,p.DataSourceId);Assert.True(p.IsActive);Assert.NotNull(p.AmenitiesVerifiedAt);});
        // A source removed after preview invalidates the entire confirmed selection.
        var second = await admin.PostAsync("/AdminBulk/Preview",Form(("__RequestVerificationToken",token),("Target","Bins"),("Action","AssignSource"),("DataSourceId","2"),("Ids","1"),("Ids","2")));
        var secondToken = Field(await second.Content.ReadAsStringAsync(),"token");
        await db.DataSources.Where(s=>s.Id==2).ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminBulk/Apply",Form(("__RequestVerificationToken",token),("token",secondToken)))).StatusCode);
        Assert.All(await db.TrashBins.AsNoTracking().ToListAsync(),b=>Assert.Equal(1,b.DataSourceId));
    }

    [Fact]
    public async Task ExpiredOrOtherUserConfirmationAndInvalidSelectionsCannotMutate()
    {
        using var admin = Client("admin"); var token = Token(await Page(admin,"/AdminPlaces"));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("DoggyDrop.AdminBulk.v1");
        foreach (var ticket in new[] {
            new DoggyDrop.ViewModels.BulkTicket(new() { Target=DoggyDrop.ViewModels.BulkTarget.Places,Action=DoggyDrop.ViewModels.BulkAction.Deactivate,Ids=[1] },"admin",DateTime.UtcNow.AddMinutes(-1)),
            new DoggyDrop.ViewModels.BulkTicket(new() { Target=DoggyDrop.ViewModels.BulkTarget.Places,Action=DoggyDrop.ViewModels.BulkAction.Deactivate,Ids=[1] },"different-admin",DateTime.UtcNow.AddMinutes(10)) })
        {
            var encrypted = protector.Protect(System.Text.Json.JsonSerializer.Serialize(ticket));
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBulk/Apply",Form(("__RequestVerificationToken",token),("token",encrypted)))).StatusCode);
        }
        foreach (var ids in new[] { new[]{"1","999"}, Enumerable.Repeat("1",101).ToArray(),new[]{"not-an-id"} })
        {
            var values = new List<(string,string)> { ("__RequestVerificationToken",token),("Target","Places"),("Action","Deactivate") };
            values.AddRange(ids.Select(id=>("Ids",id)));
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBulk/Preview",Form(values.ToArray()))).StatusCode);
        }
        Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>Assert.True(p.IsActive));
    }

    [Fact]
    public async Task SourceDeleteHasReadOnlyPreviewAndPostPreservesContent()
    {
        using var admin = Client("admin"); var page = await Page(admin,"/AdminDataSources/Delete/1");
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(await db.DataSources.AnyAsync(s=>s.Id==1));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminDataSources/Delete/1",Form(("__RequestVerificationToken",Token(page))))).StatusCode);
        Assert.Equal(2,await db.Places.CountAsync()); Assert.Equal(2,await db.TrashBins.CountAsync());
        Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>Assert.Null(p.DataSourceId));
        Assert.All(await db.TrashBins.AsNoTracking().ToListAsync(),p=>Assert.Null(p.DataSourceId));
    }

    [Fact]
    public async Task AdminPagesRenderSourcesCandidatesAndFormsWhilePublicPagesKeepMetadataPrivate()
    {
        using var admin = Client("admin"); using var anon = Client(); using var user = Client("alice");
        foreach (var (path,name) in new[] { ("/AdminDataSources","sources-list"),("/AdminDataSources/Edit/1","source-edit"),
            ("/AdminBins","bins-list"),("/AdminPlaces","places-list"),("/AdminDuplicates?target=Bins&latitude=46&longitude=15","bin-duplicates"),
            ("/AdminDuplicates?target=Places&latitude=46&longitude=15","place-duplicates") })
        {
            var html=await Page(admin,path); await Capture(name,html);
            if(path.Contains("Duplicates")) { Assert.Contains("Približno",WebUtility.HtmlDecode(html)); Assert.Contains("Občina",WebUtility.HtmlDecode(html)); }
            else if(path.Contains("Edit/")) { Assert.Contains("INTERNAL-CONTACT",html); Assert.Contains("INTERNAL-NOTES",html); }
        }
        foreach(var html in new[]{await Page(anon,"/Places"),await Page(anon,"/Places/Details/1"),await Page(user,"/SavedPlaces")})
        {
            Assert.DoesNotContain("INTERNAL-CONTACT",html); Assert.DoesNotContain("INTERNAL-NOTES",html);
            Assert.DoesNotContain("private-contact@example.com",html); Assert.DoesNotContain("internal-source.example",html);
        }
    }
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
