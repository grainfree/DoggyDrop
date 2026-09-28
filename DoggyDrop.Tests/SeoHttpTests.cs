using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// Isolated MVC host: never invokes application startup, migrations or external services.
public sealed class SeoHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"seo-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root != null && !File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj"))) root=root.Parent;
        Assert.NotNull(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName=typeof(PlacesController).Assembly.GetName().Name, EnvironmentName="Production",
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"), Args=[]
        });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Configuration["Seo:AllowIndexing"]="true"; builder.Configuration["IS_PULL_REQUEST"]="false";
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddSingleton<IGamificationCalendar, GamificationCalendar>();
        builder.Services.AddScoped<IWeeklyGoalsService, WeeklyGoalsService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test";})
            .AddScheme<AuthenticationSchemeOptions, TestAuth>("Test",_=>{});
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PlacesController).Assembly).AddControllersAsServices();
        builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp=>new MapController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IWebHostEnvironment>(),sp.GetRequiredService<UserManager<ApplicationUser>>(),
            null!,null!,null!,null!,null!,null!,null!,null!,null!));
        builder.Services.AddTransient(sp=>new HomeController(sp.GetRequiredService<ILogger<HomeController>>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),null!,null!,sp.GetRequiredService<ApplicationDbContext>(),null!,null!,null!,null!,null!));
        app=builder.Build();
        app.UseMiddleware<SeoIndexingMiddleware>(); app.UseExceptionHandler("/Home/Error");
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders=ForwardedHeaders.XForwardedHost|ForwardedHeaders.XForwardedProto });
        app.UseStaticFiles(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}"); app.MapRazorPages();
        app.MapGet("/test-error", (Func<string>)(()=>throw new InvalidOperationException("Do not disclose this test exception")));
        app.MapGet("/health",()=>new {status="ok"});
        await using(var scope=app.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.TrashBins.Add(new TrashBin {Name="Public bin",IsApproved=true,Latitude=46,Longitude=15});
            db.Users.Add(new ApplicationUser {Id="alice",UserName="Alice"});
            db.Places.AddRange(new Place {Id=1,Name="Čuvaj – Pasji park",Category=PlaceCategory.DogPark,Latitude=46,Longitude=15,
                Address="Naslov 1, Maribor",Description="Resnični opis",AmenitiesSourceUrl="https://private.example/source",AmenitiesVerifiedAt=DateTime.UtcNow,
                Amenities=[new PlaceAmenity {AmenityType=PlaceAmenityType.WaterForDogs}]},
                new Place {Id=2,Name="Hidden",Category=PlaceCategory.PetShop,IsActive=false},
                new Place {Id=3,Name="Corrupt",Category=(PlaceCategory)99},
                new Place {Id=4,Name="Bad coords",Category=PlaceCategory.DogPark,Latitude=91});
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string host="doggydrop.app", bool signedIn=false)
    {
        var client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false}) {BaseAddress=new Uri(app.Urls.Single())};
        client.DefaultRequestHeaders.Host=host;
        if(signedIn)client.DefaultRequestHeaders.Add("X-Test-User","alice");
        return client;
    }
    private static string Canonical(string html) => WebUtility.HtmlDecode(Regex.Match(html,"<link rel=\"canonical\" href=\"([^\"]+)\"").Groups[1].Value);
    private static string Json(string html) => Regex.Match(html,"<script type=\"application/ld\\+json\">([\\s\\S]*?)</script>").Groups[1].Value;
    private static void Robots(HttpResponseMessage response,string expected) => Assert.Equal(expected,Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
    private async Task Mutate(Action<Place> mutation)
    {
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        mutation(await db.Places.SingleAsync(p=>p.Id==1));await db.SaveChangesAsync();
    }
    [Theory]
    [InlineData("/")]
    [InlineData("/Map")]
    [InlineData("/Map/Index?placeId=1&navigate=1")]
    public async Task HomeAliasesKeepOneCanonicalAndPwaMetadata(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();Robots(response,"index, follow");
        Assert.Equal("https://doggydrop.app/",Canonical(html));Assert.Contains("zemljevid za sprehode s psom",WebUtility.HtmlDecode(html));
        Assert.Single(Regex.Matches(html,"<title>"));Assert.Single(Regex.Matches(html,"name=\"description\""));
        Assert.Contains("<html lang=\"sl\">",html);Assert.Contains("href=\"/manifest.json\"",html);Assert.Contains("/images/icon-192.png",html);
        Assert.Contains("home-intro.js",html);Assert.Empty(Json(html));
        await Capture("seo-home",html);
    }
    [Fact]
    public async Task DiscoveryHasCleanMetadataAndNoFilterVariants()
    {
        using var client=Client();var response=await client.GetAsync("/Places?category=1&utm_source=test");
        var html=await response.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("https://doggydrop.app/Places",Canonical(html));Robots(response,"index, follow");
        Assert.Contains("Pasje lokacije | DoggyDrop",html);Assert.Empty(Json(html));
        await Capture("seo-discovery",html);
    }
    [Theory]
    [InlineData("/Places/Details/1")]
    [InlineData("/Places/Details/1?returnUrl=//evil.example")]
    [InlineData("/lokacije/1/cuvaj-pasji-park")]
    [InlineData("/lokacije/1/cuvaj-pasji-park?utm_source=search")]
    public async Task DirectPublicDetailsHaveSafeMetadataAndNoPrivateFacts(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();Robots(response,"index, follow");
        Assert.Equal("https://doggydrop.app/lokacije/1/cuvaj-pasji-park",Canonical(html));
        Assert.Single(Regex.Matches(html,"<title>"));Assert.Single(Regex.Matches(html,"name=\"description\""));
        Assert.Contains("Razišči lokacije",WebUtility.HtmlDecode(html));Assert.Contains("Navodila za pot",html);
        Assert.Contains("placeDetailsMap",html);Assert.Contains("Voda za pse",html);
        Assert.DoesNotContain("home-intro.js",html);Assert.DoesNotContain("private.example",html);
        using var json=JsonDocument.Parse(Json(html));Assert.Equal("Park",json.RootElement.GetProperty("@type").GetString());
        Assert.Equal(Canonical(html),json.RootElement.GetProperty("url").GetString());
        await Capture("seo-place",html);
    }
    [Theory]
    [InlineData("/lokacije/1/wrong-slug")]
    [InlineData("/lokacije/1")]
    [InlineData("/lokacije/1/WRONG")]
    [InlineData("/lokacije/1/wrong/extra")]
    public async Task WrongOrMalformedSlugRedirectsLocally(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.MovedPermanently,response.StatusCode);
        Assert.Equal("/lokacije/1/cuvaj-pasji-park",response.Headers.Location!.OriginalString);
    }
    [Fact]
    public async Task RenameKeepsIdAndOldSlugRedirectsToNewName()
    {
        await Mutate(p=>p.Name="Nov park Šentjur");using var client=Client();
        var response=await client.GetAsync("/lokacije/1/cuvaj-pasji-park");
        Assert.Equal(HttpStatusCode.MovedPermanently,response.StatusCode);Assert.Equal("/lokacije/1/nov-park-sentjur",response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/Places/Details/1")).StatusCode);
    }
    [Theory]
    [InlineData("/Places/Details/2")]
    [InlineData("/lokacije/2/hidden")]
    [InlineData("/Places/Details/3")]
    [InlineData("/lokacije/3/corrupt")]
    [InlineData("/Places/Details/4")]
    [InlineData("/lokacije/999/missing")]
    [InlineData("/not-a-route")]
    public async Task IneligibleAndMissingContentIs404Noindex(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        Robots(response,"noindex, nofollow");Assert.Empty(Json(await response.Content.ReadAsStringAsync()));
    }
    [Fact]
    public async Task AdminTextCannotEscapeHtmlOrJsonScriptBoundaries()
    {
        var name="Park </script><script>alert(1)</script> \"Č\"";
        await Mutate(p=>{p.Name=name;p.Address="<img src=x onerror=alert(2)>";p.Description="<script>alert(3)</script>";});
        using var client=Client();var response=await client.GetAsync("/Places/Details/1");var html=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("<script>alert(",html);Assert.DoesNotContain("<img src=x",html);
        Assert.Single(Regex.Matches(html,"<script type=\"application/ld\\+json\">"));
        using var json=JsonDocument.Parse(Json(html));Assert.Equal(name,json.RootElement.GetProperty("name").GetString());
    }
    [Fact]
    public async Task HostAndForwardedHostCannotPoisonMetadataOrEnablePreviewIndexing()
    {
        using var client=Client("evil.example");client.DefaultRequestHeaders.Add("X-Forwarded-Host","doggydrop.app");
        var response=await client.GetAsync("/Places/Details/1");var html=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Robots(response,"noindex, nofollow");
        Assert.Equal("https://doggydrop.app/lokacije/1/cuvaj-pasji-park",Canonical(html));Assert.DoesNotContain("evil.example",Json(html));
        var robots=await client.GetStringAsync("/robots.txt");Assert.Contains("Disallow: /",robots);Assert.Contains("Sitemap: https://doggydrop.app/sitemap.xml",robots);
        var xml=XDocument.Parse(await client.GetStringAsync("/sitemap.xml"));Assert.Empty(xml.Root!.Elements());
    }
    [Theory]
    [InlineData("Development",false,true)]
    [InlineData("Staging",false,true)]
    [InlineData("Production",true,true)]
    [InlineData("Production",false,false)]
    public async Task NonProductionPreviewAndOptOutDiscourageIndexing(string environment,bool preview,bool enabled)
    {
        app.Environment.EnvironmentName=environment;app.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName=environment;app.Configuration["IS_PULL_REQUEST"]=preview.ToString();app.Configuration["Seo:AllowIndexing"]=enabled.ToString();
        using var client=Client();var response=await client.GetAsync("/Places/Details/1");Robots(response,"noindex, nofollow");
        Assert.Contains("noindex, nofollow",await response.Content.ReadAsStringAsync());Assert.Contains("Disallow: /",await client.GetStringAsync("/robots.txt"));
        Assert.Empty(XDocument.Parse(await client.GetStringAsync("/sitemap.xml")).Root!.Elements());
    }
    [Theory]
    [InlineData("/SavedPlaces")]
    [InlineData("/Walks")]
    [InlineData("/Walks/Active/1")]
    [InlineData("/Walks/Details/1")]
    [InlineData("/Home/UserProfile")]
    [InlineData("/Home/Settings")]
    [InlineData("/Home/Community")]
    [InlineData("/AdminPlaces")]
    [InlineData("/AdminBinImport")]
    public async Task AuthorizedSurfacesRemainProtectedAndNoindex(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);Robots(response,"noindex, nofollow");
    }
    [Theory]
    [InlineData("/Identity/Account/Login")]
    [InlineData("/Identity/Account/Register")]
    [InlineData("/Home/Privacy")]
    [InlineData("/health")]
    [InlineData("/Map/GetNearestBin?latitude=46&longitude=15")]
    public async Task UtilityPagesAndApisAreNoindex(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Robots(response,"noindex, nofollow");
    }
    [Theory]
    [InlineData("/Home/Error")]
    [InlineData("/test-error")]
    public async Task ErrorsKeep500AndNoindexWithoutExceptionDetails(string path)
    {
        using var client=Client();var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode);
        Robots(response,"noindex, nofollow");var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("Do not disclose",html);Assert.DoesNotContain("rel=\"canonical\"",html);
    }
    [Fact]
    public async Task SitemapAndRobotsContainOnlyCanonicalPublicDestinations()
    {
        using var client=Client();var response=await client.GetAsync("/sitemap.xml");Assert.Equal("application/xml",response.Content.Headers.ContentType!.MediaType);
        var xml=XDocument.Parse(await response.Content.ReadAsStringAsync());XNamespace ns="http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(new[]{"https://doggydrop.app/","https://doggydrop.app/Places","https://doggydrop.app/lokacije/1/cuvaj-pasji-park"},xml.Descendants(ns+"loc").Select(x=>x.Value));
        var robots=await client.GetStringAsync("/robots.txt");Assert.Contains("Allow: /",robots);Assert.DoesNotContain("Disallow",robots);
        Assert.Contains("Sitemap: https://doggydrop.app/sitemap.xml",robots);
        var image=await client.GetAsync("/images/icon-512.png");Assert.Equal(HttpStatusCode.OK,image.StatusCode);Assert.False(image.Headers.Contains("X-Robots-Tag"));
    }
    [Fact]
    public async Task SavedPersonalizationDoesNotChangeCanonicalOrStructuredData()
    {
        using var anon=Client();using var user=Client(signedIn:true);
        await using(var scope=app.Services.CreateAsyncScope()) {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();db.SavedPlaces.Add(new SavedPlace {UserId="alice",PlaceId=1});await db.SaveChangesAsync();
        }
        var publicHtml=await anon.GetStringAsync("/Places/Details/1");var response=await user.GetAsync("/Places/Details/1");var personalHtml=await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Robots(response,"noindex, nofollow");Assert.Equal(Canonical(publicHtml),Canonical(personalHtml));Assert.Equal(Json(publicHtml),Json(personalHtml));
    }
    private static async Task Capture(string name,string html)
    {
        var dir=Environment.GetEnvironmentVariable("DOGGYDROP_SEO_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(dir))return;
        Directory.CreateDirectory(dir);await File.WriteAllTextAsync(Path.Combine(dir,name+".html"),html);
    }
    public async Task DisposeAsync(){if(app!=null)await app.DisposeAsync();if(File.Exists(database))File.Delete(database);}
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(Request.Headers["X-Test-User"]!="alice")return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"alice"),new Claim(ClaimTypes.Name,"Alice")],IdentityConstants.ApplicationScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
}
