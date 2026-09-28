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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// This host never invokes Program, migrations, email, map or storage providers.
public sealed partial class PrivacyHttpTests : IAsyncLifetime
{
    private const string Manage = "/Identity/Account/Manage/";
    private const string Password = "Synthetic-test-Pass!78";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"privacy-http-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj"))) root=root.Parent;
        Assert.NotNull(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName=typeof(HomeController).Assembly.GetName().Name, EnvironmentName="Production",
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"), Args=[] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Logging.ClearProviders(); builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Error); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=> { o.DefaultAuthenticateScheme="Test"; o.DefaultChallengeScheme="Test"; })
            .AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});
        builder.Services.AddScoped<PersonalDataExport>(); builder.Services.AddScoped<AccountDataDeletion>(); builder.Services.AddScoped<WalkDataDeletion>();
        builder.Services.AddSingleton<IUserMediaCleanup,NoopCleanup>();
        builder.Services.AddScoped<ILocalLeaderboardService,LocalLeaderboardService>();
        builder.Services.AddScoped<INotificationService,NotificationService>();
        builder.Services.AddSingleton<IGamificationCalendar,GamificationCalendar>();
        builder.Services.AddScoped<IWeeklyGoalsService,WeeklyGoalsService>();
        builder.Services.AddScoped<IGamificationService,GamificationService>();
        builder.Services.AddScoped<IDogProgressionService,DogProgressionService>();
        builder.Services.AddScoped<IUserAchievementService,UserAchievementService>();
        builder.Services.AddSingleton<ISeasonalEventService,SeasonalEventService>();
        builder.Services.AddSingleton<IMapStampService,MapStampService>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly).AddControllersAsServices();
        builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp=>new HomeController(sp.GetRequiredService<ILogger<HomeController>>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),null!,null!,sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IGamificationService>(),sp.GetRequiredService<ISeasonalEventService>(),
            sp.GetRequiredService<ILocalLeaderboardService>(),sp.GetRequiredService<IMapStampService>(),sp.GetRequiredService<IUserAchievementService>()));
        builder.Services.AddTransient(sp=>new WalksController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),sp.GetRequiredService<INotificationService>(),null!,
            sp.GetRequiredService<IGamificationService>(),sp.GetRequiredService<IDogProgressionService>(),null!,null!,
            sp.GetRequiredService<IGamificationCalendar>(),sp.GetRequiredService<IUserAchievementService>()));
        builder.Services.AddTransient(sp=>new DogsController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),null!,sp.GetRequiredService<IDogProgressionService>(),
            sp.GetRequiredService<IUserAchievementService>()));
        app=builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Home}/{action=Index}/{id?}"); app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
            await PrivacyLifecycleTests.Seed(db);
            var user=await db.Users.SingleAsync(x=>x.Id=="alice");
            user.PasswordHash=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().PasswordHasher.HashPassword(user,Password);
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user=null)
    {
        var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(app.Urls.Single()) };
        if(user!=null) client.DefaultRequestHeaders.Add("X-Test-User",user);
        return client;
    }
    private static async Task<string> Token(HttpClient client,string path)
    {
        var response=await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token); return token;
    }
    [Theory]
    [InlineData("PersonalData")]
    [InlineData("DownloadPersonalData")]
    [InlineData("DeletePersonalData")]
    public async Task PersonalDataRequiresAuthentication(string page)
    {
        using var client=Client(); Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(Manage+page)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsync(Manage+page,new FormUrlEncodedContent([]))).StatusCode);
    }
    [Theory]
    [InlineData("DownloadPersonalData")]
    [InlineData("DeletePersonalData")]
    public async Task MutationsRetainAntiforgery(string page)
    {
        using var client=Client("alice");
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsync(Manage+page,new FormUrlEncodedContent([]))).StatusCode);
    }
    [Fact]
    public async Task ExportResponseIsPrivateJsonAndCannotSelectAnotherUser()
    {
        using var client=Client("alice"); var token=await Token(client,Manage+"PersonalData");
        var response=await client.PostAsync(Manage+"DownloadPersonalData?userId=bob",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=token,["userId"]="bob" }));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("application/json",response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore); Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("DoggyDrop-osebni-podatki.json",response.Content.Headers.ContentDisposition.FileName);
        var json=await response.Content.ReadAsStringAsync(); using var doc=JsonDocument.Parse(json);
        Assert.Equal("alice",doc.RootElement.GetProperty("Account").GetProperty("Id").GetString());
        Assert.Equal(1201,doc.RootElement.GetProperty("WalkPoints").GetArrayLength());
        Assert.Equal("walk-image",Assert.Single(doc.RootElement.GetProperty("WalkPhotos").EnumerateArray()).GetProperty("ImageUrl").GetString());
        Assert.Single(doc.RootElement.GetProperty("Walks").EnumerateArray());
        Assert.DoesNotContain("88.7654321",json);
        Assert.DoesNotContain("bob@example.invalid",json); Assert.DoesNotContain("authentication-secret",json); Assert.DoesNotContain("token-secret",json);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Manage+"DownloadPersonalData")).StatusCode);
    }
    [Theory]
    [InlineData("alice",false,Password,false)]
    [InlineData("alice",true,"wrong-password",false)]
    [InlineData("alice",true,Password,true)]
    [InlineData("bob",false,"",false)]
    [InlineData("bob",true,"",true)]
    public async Task DeletePreservesPasswordAndExplicitConfirmation(string user,bool confirmed,string password,bool succeeds)
    {
        using var client=Client(user); var token=await Token(client,Manage+"DeletePersonalData");
        var response=await client.PostAsync(Manage+"DeletePersonalData",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=token,["Input.ConfirmDeletion"]=confirmed.ToString(),["Input.Password"]=password }));
        Assert.Equal(succeeds?HttpStatusCode.Redirect:HttpStatusCode.OK,response.StatusCode);
        await using var scope=app.Services.CreateAsyncScope();
        Assert.Equal(!succeeds,await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.AnyAsync(x=>x.Id==user));
        if(!succeeds) Assert.Contains(confirmed?"Geslo ni pravilno":"Potrdi",await response.Content.ReadAsStringAsync());
        if(succeeds && user=="alice")
        {
            using var anonymous=Client(); var bins=await anonymous.GetStringAsync("/api/trashbins/nearby");
            using var doc=JsonDocument.Parse(bins); var bin=Assert.Single(doc.RootElement.EnumerateArray());
            Assert.Equal("Approved",bin.GetProperty("name").GetString());
            Assert.Equal("DoggyDrop uporabnik",bin.GetProperty("addedBy").GetString());
            Assert.DoesNotContain("alice",bins,StringComparison.OrdinalIgnoreCase);
            using var friend=Client("bob");
            Assert.Equal("[]",await friend.GetStringAsync("/api/friends"));
            Assert.Equal("[]",await friend.GetStringAsync("/api/friends/requests"));
        }
    }
    [Fact]
    public async Task PhotoDeletionEndpointRequiresOwnershipAndAntiforgery()
    {
        int walkId, photoId;
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var photo=await db.WalkPhotos.SingleAsync();
            walkId=photo.WalkId; photoId=photo.Id;
        }
        using var other=Client("bob"); var otherToken=await Token(other,Manage+"PersonalData");
        var fields=new Dictionary<string,string> { ["id"]=walkId.ToString(),["photoId"]=photoId.ToString(),["__RequestVerificationToken"]=otherToken };
        Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync("/Walks/DeletePhoto",new FormUrlEncodedContent(fields))).StatusCode);
        using var owner=Client("alice");
        fields.Remove("__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsync("/Walks/DeletePhoto",new FormUrlEncodedContent(fields))).StatusCode);
        fields["__RequestVerificationToken"]=await Token(owner,Manage+"PersonalData");
        Assert.Equal(HttpStatusCode.Redirect,(await owner.PostAsync("/Walks/DeletePhoto",new FormUrlEncodedContent(fields))).StatusCode);
        await using var finalScope=app.Services.CreateAsyncScope();
        Assert.Empty(await finalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().WalkPhotos.ToListAsync());
    }
    [Theory]
    [InlineData(null,false)] [InlineData("",true)] [InlineData(" ",false)]
    public async Task AnonymousLeaderboardNeverUsesAccountIdentifier(string? display,bool zone)
    {
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.Where(x=>x.Id=="alice").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DisplayName,display)
                .SetProperty(x=>x.Email,"identifiable@example.com").SetProperty(x=>x.UserName,"identifiable@example.com"));
            if(!zone) await db.PrivacyZones.Where(x=>x.UserId=="alice").ExecuteDeleteAsync();
        }
        using var client=Client(); var response=await client.GetAsync("/api/leaderboards/local?city=maribor");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); var json=await response.Content.ReadAsStringAsync();
        Assert.Contains("Uporabnik",json); Assert.DoesNotContain("identifiable",json); Assert.DoesNotContain("email",json,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("/api/friends")]
    [InlineData("/api/friends/requests")]
    public async Task FriendDtosNeverUseEmail(string path)
    {
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Users.Where(x=>x.Id=="alice").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DisplayName,(string?)null).SetProperty(x=>x.Email,"identifiable@example.com"));
            if(path.EndsWith("requests")) await db.Friendships.Where(x=>x.RequesterId=="alice").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Pending"));
        }
        using var client=Client("bob"); var response=await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=await response.Content.ReadAsStringAsync(); Assert.Contains("Uporabnik",json); Assert.DoesNotContain("identifiable",json);
        Assert.DoesNotContain("email",json,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task WalkSocialDtoHasNeutralAuthorAndNoRoute()
    {
        int walkId;
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            walkId=await db.Walks.Where(x=>x.OwnerId=="alice").Select(x=>x.Id).SingleAsync();
            await db.Users.Where(x=>x.Id=="alice").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DisplayName,(string?)null).SetProperty(x=>x.Email,"identifiable@example.com"));
            db.WalkComments.Add(new WalkComment { UserId="alice",WalkId=walkId,Body="Test comment" }); await db.SaveChangesAsync();
        }
        using var other=Client("bob");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/api/walks/{walkId}/social")).StatusCode);
        using var client=Client("alice"); var response=await client.GetAsync($"/api/walks/{walkId}/social");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); var json=await response.Content.ReadAsStringAsync();
        Assert.Contains("Uporabnik",json); Assert.DoesNotContain("identifiable",json); Assert.DoesNotContain("latitude",json,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task SettingsAndTermsRenderAccurateScope()
    {
        using var client=Client("alice");
        var settings=WebUtility.HtmlDecode(await client.GetStringAsync("/Home/Settings"));
        Assert.Contains("zemljevidu skupnosti",settings); Assert.Contains("dostopne samo tebi",settings);
        Assert.Contains("fotografij",settings);
        using var anonymous=Client(); var terms=await anonymous.GetAsync("/Home/Terms");
        Assert.Equal(HttpStatusCode.OK,terms.StatusCode);
        Assert.Contains("JSON",await terms.Content.ReadAsStringAsync());
    }
    public async Task DisposeAsync(){if(app!=null)await app.DisposeAsync();if(File.Exists(database))File.Delete(database);}
    private sealed class NoopCleanup : IUserMediaCleanup { public Task CleanupAsync(IEnumerable<string?> urls)=>Task.CompletedTask; }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user=Request.Headers["X-Test-User"].ToString();
            if(user is not ("alice" or "bob"))return Task.FromResult(AuthenticateResult.NoResult());
            var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user+"@example.invalid")],IdentityConstants.ApplicationScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,Scheme.Name)));
        }
    }
}
