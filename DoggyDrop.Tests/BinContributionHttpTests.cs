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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinContributionHttpTests : IAsyncLifetime
{
    private readonly string file=Path.Combine(Path.GetTempPath(),"bin-community-http-"+Guid.NewGuid()+".db");
    private WebApplication app=null!;
    private readonly Uploads uploads=new();
    public async Task InitializeAsync()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj")))root=root.Parent;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(MapController).Assembly.GetName().Name,ContentRootPath=Path.Combine(root!.FullName,"DoggyDrop"),EnvironmentName="Testing",Args=[] });
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={file};Pooling=False"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test";}).AddScheme<AuthenticationSchemeOptions,UserAuth>("Test",_=>{});
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));builder.Services.AddSingleton<IGamificationCalendar,GamificationCalendar>();
        builder.Services.AddScoped<IGamificationService,GamificationService>();builder.Services.AddScoped<INotificationService,NotificationService>();builder.Services.AddScoped<IWeeklyGoalsService,WeeklyGoalsService>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);builder.Services.AddSingleton<BinSubmissionLimits>();
        builder.Services.AddSingleton<ICloudinaryService>(uploads);builder.Services.AddSingleton<IBinPhotoStorage>(uploads);
        builder.Services.AddScoped<IBinPhotoReferences,BinPhotoReferences>();builder.Services.AddScoped<BinContributions>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MapController).Assembly).AddControllersAsServices();builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp=>new MapController(sp.GetRequiredService<ApplicationDbContext>(),sp.GetRequiredService<IWebHostEnvironment>(),sp.GetRequiredService<UserManager<ApplicationUser>>(),uploads,null!,sp.GetRequiredService<INotificationService>(),sp.GetRequiredService<IGamificationService>(),null!,null!,null!,sp.GetRequiredService<IGamificationCalendar>(),null!));
        builder.Services.AddTransient(sp => new WalksController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, null!, null!, null!, null!,
            sp.GetRequiredService<IGamificationCalendar>(), null!));
        app=builder.Build();app.UseStaticFiles();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser{Id="user",UserName="PRIVATE USER"},new ApplicationUser{Id="admin",UserName="PRIVATE ADMIN"});
            db.DataSources.Add(new DataSource{Id=1,Name="OpenStreetMap — koši za pasje iztrebke",ContactEmail="private-source@example.test",Notes="PRIVATE SOURCE NOTE"});
            db.TrashBins.AddRange(new TrashBin{Id=1,Name="OSM bin",Latitude=46,Longitude=15,IsApproved=true,DataSourceId=1},
                new TrashBin{Id=2,Name="Retired bin",Latitude=46,Longitude=15.01,IsApproved=true,IsRetired=true},
                new TrashBin{Id=3,Name="Pending bin",Latitude=46,Longitude=15.02});await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user=null){var c=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};if(user!=null)c.DefaultRequestHeaders.Add("X-Test-User",user);return c;}
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string token,params (string,string)[] values)=>new(values.Select(v=>new KeyValuePair<string,string>(v.Item1,v.Item2)).Append(new("__RequestVerificationToken",token)));
    private static string HomeToken(string html) => System.Text.Json.JsonSerializer.Deserialize<string>(
        Regex.Match(html, "const requestVerificationToken = (.*?);").Groups[1].Value)!;

    [Theory]
    [InlineData("used")] [InlineData("full")] [InlineData("useful")] [InlineData("not-useful")]
    public async Task ActualHomeTokenAndUnambiguousActionMutateOnlyIntendedCounter(string action)
    {
        using var user = Client("user");
        var home = await user.GetStringAsync("/");
        var token = HomeToken(home); Assert.NotEmpty(token);
        // Same URL/form as Home, with a hostile legacy query to prove it cannot override the field.
        var response = await user.PostAsync("/Map/BinAction/1?action=missing&binAction=missing",
            Form(token, ("binAction", action)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bin = (await db.TrashBins.FindAsync(1))!;
        Assert.Equal(action == "used" ? 1 : 0, bin.UsedCount);
        Assert.Equal(action == "full" ? 1 : 0, bin.FullReports);
        Assert.Equal(action == "useful" ? 1 : 0, bin.UsefulVotes);
        Assert.Equal(action == "not-useful" ? 1 : 0, bin.NotUsefulVotes);
        Assert.Equal(0, bin.MissingReports); Assert.False(bin.IsRetired);
    }

    [Theory]
    [InlineData("")] [InlineData("invalid")]
    public async Task HomeActionWithMissingOrInvalidTokenDoesNotMutate(string token)
    {
        using var user = Client("user"); await user.GetStringAsync("/");
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/Map/BinAction/1", Form(token, ("binAction", "used")))).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(0, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.FindAsync(1))!.UsedCount);
    }

    [Fact]
    public async Task AnonymousHomeActionRemainsDeniedEvenWithItsValidToken()
    {
        using var anon = Client(); var token = HomeToken(await anon.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/Map/BinAction/1", Form(token, ("binAction", "used")))).StatusCode);
    }

    [Theory]
    [InlineData("BIN_MISSING")] [InlineData("WRONG_LOCATION")] [InlineData("DAMAGED")]
    [InlineData("NOT_PUBLIC")] [InlineData("DUPLICATE")] [InlineData("OTHER")]
    public async Task HomeIssueEntry_AllReasonsUseProtectedFormAndRetryKeepsOnePendingIssue(string reason)
    {
        await using (var scope = app.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TrashBins.Add(new TrashBin { Id = 4, Name = "Other active", IsApproved = true, Latitude = 46.1, Longitude = 15 });
            await db.SaveChangesAsync();
        }
        using var user = Client("user"); var home = await user.GetStringAsync("/");
        Assert.Contains("/BinContributions/Create/", home);
        var page = await user.GetStringAsync("/BinContributions/Create/1");
        var values = new List<(string, string)> { ("BinId", "1"), ("Type", "Issue"), ("Reason", reason), ("RequestId", Guid.NewGuid().ToString()), ("Description", "Opis težave") };
        if (reason == "WRONG_LOCATION") { values.Add(("Latitude", "46.002")); values.Add(("Longitude", "15")); }
        if (reason == "DUPLICATE") values.Add(("DuplicateBinId", "4"));
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.Redirect,
            (await user.PostAsync("/BinContributions/Create", Form(Token(page), values.ToArray()))).StatusCode);
        await using var check = app.Services.CreateAsyncScope();
        var current = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(await current.BinContributions.ToListAsync());
        var bin = (await current.TrashBins.FindAsync(1))!;
        Assert.Equal(46, bin.Latitude); Assert.False(bin.IsRetired); Assert.Equal(0, bin.MissingReports);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DashboardAndAnalyticsCountPendingSeparatelyFromPublicInfrastructure(bool analytics)
    {
        await using (var scope = app.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.TrashBins.AddRange(new TrashBin { Id = 4, Name = "Second active", IsApproved = true, Latitude = 46.1, Longitude = 15 },
                new TrashBin { Id = 5, Name = "Rejected", IsRejected = true, Latitude = 46.2, Longitude = 15 });
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.TrashBins.PublicBins().CountAsync());
            Assert.Equal(1, await db.TrashBins.PendingBins().CountAsync());
            if (!analytics) {
                var result = Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(await new CityController(db).Dashboard());
                var model = Assert.IsType<DoggyDrop.ViewModels.CityDashboardViewModel>(result.Model);
                Assert.Equal(2, model.TotalBins); Assert.Equal(2, model.ApprovedBins); Assert.Equal(1, model.PendingBins);
                return;
            }
        }
        using var anon = Client();
        using var json = System.Text.Json.JsonDocument.Parse(await anon.GetStringAsync("/api/analytics/summary"));
        var bins = json.RootElement.GetProperty("bins");
        Assert.Equal(2, bins.GetProperty("total").GetInt32()); Assert.Equal(2, bins.GetProperty("approved").GetInt32());
        Assert.Equal(1, bins.GetProperty("pending").GetInt32());
        Assert.DoesNotContain("Pending bin", json.RootElement.GetProperty("topBins").ToString());
        Assert.DoesNotContain("Retired bin", json.RootElement.GetProperty("topBins").ToString());
        Assert.DoesNotContain("Rejected", json.RootElement.GetProperty("topBins").ToString());
    }
    [Theory]
    [InlineData(null,"/BinContributions/Create/1",401)] [InlineData("user","/BinContributions/Create/1",200)] [InlineData("admin","/BinContributions/Create/1",200)]
    [InlineData(null,"/AdminBinContributions",401)] [InlineData("user","/AdminBinContributions",403)] [InlineData("admin","/AdminBinContributions",200)]
    [InlineData(null,"/AdminBinContributions/Lifecycle/1",401)] [InlineData("user","/AdminBinContributions/Lifecycle/1",403)] [InlineData("admin","/AdminBinContributions/Lifecycle/1",200)]
    [InlineData(null,"/Map/Add",200)] [InlineData("user","/Map/Add",200)] [InlineData("admin","/Map/Add",200)]
    public async Task AuthorizationMatrix(string? user,string path,int status)
    {using var c=Client(user);Assert.Equal(status,(int)(await c.GetAsync(path)).StatusCode);}
    [Theory]
    [InlineData("user","/BinContributions/Create")] [InlineData("admin","/AdminBinContributions/Review")]
    [InlineData("admin","/AdminBinContributions/Lifecycle")] [InlineData(null,"/Map/Add")]
    [InlineData("admin","/Map/Delete/1")] [InlineData("user","/Map/BinAction/1")]
    public async Task MissingAndInvalidAntiforgeryCannotMutate(string? user,string path)
    {using var c=Client(user);foreach(var token in new[]{"","invalid"})Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync(path,Form(token))).StatusCode);}
    [Theory]
    [InlineData(null,"/BinContributions/Create",401)] [InlineData(null,"/AdminBinContributions/Review",401)]
    [InlineData("user","/AdminBinContributions/Review",403)] [InlineData("user","/AdminBinContributions/Lifecycle",403)] [InlineData("user","/Map/Delete/1",403)]
    public async Task MutationAuthorizationPrecedesBinding(string? user,string path,int expected)
    {using var c=Client(user);Assert.Equal(expected,(int)(await c.PostAsync(path,Form(""))).StatusCode);}
    [Fact]
    public async Task PendingIssueIsPrivateEncodedAndNotPublicEvidence()
    {
        using var user=Client("user");var form=await user.GetStringAsync("/BinContributions/Create/1");
        var result=await user.PostAsync("/BinContributions/Create",Form(Token(form),("BinId","1"),("RequestId",Guid.NewGuid().ToString()),("Type","Issue"),("Reason","BIN_MISSING"),("Description","<script>hostile()</script>")));
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        using var admin=Client("admin");var review=await admin.GetStringAsync("/AdminBinContributions/Review/1");Assert.Contains("&lt;script&gt;",review);Assert.DoesNotContain("<script>hostile",review);
        using var anon=Client();var home=await anon.GetStringAsync("/");Assert.DoesNotContain("hostile()",home);Assert.DoesNotContain("PRIVATE SOURCE",home);Assert.DoesNotContain("private-source@",home);Assert.DoesNotContain("PRIVATE USER",home);
        var mine=await user.GetStringAsync("/BinContributions/Mine");Assert.DoesNotContain("ReviewNote",mine);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(0,(await db.TrashBins.FindAsync(1))!.MissingReports);
    }
    [Fact]
    public async Task OlderContributionsRemainReachableThroughBoundedOwnerAndAdminPages()
    {
        await using(var scope=app.Services.CreateAsyncScope()){
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for(var i=0;i<101;i++)db.BinContributions.Add(new BinContribution{BinId=1,SubmittedByUserId="user",Type=BinContributionType.Photo,RequestId=Guid.NewGuid()});
            await db.SaveChangesAsync();
        }
        foreach(var user in new[]{"user","admin"}){
            using var client=Client(user);var path=user=="admin"?"/AdminBinContributions":"/BinContributions/Mine";
            var first=await client.GetStringAsync(path);Assert.Equal(100,Regex.Matches(first,"<article>").Count);Assert.Contains("page=2",first);
            var older=await client.GetStringAsync(path+"?page=2");Assert.Single(Regex.Matches(older,"<article>").Cast<Match>());Assert.Contains("page=1",older);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(path+"?page=0")).StatusCode);
        }
    }
    [Theory]
    [InlineData("/AdminBinContributions/Review/1")] [InlineData("/AdminBinContributions/Lifecycle/1")]
    public async Task MissingAdminDecisionCannotDefaultToRejectOrReactivate(string path)
    {
        using var admin=Client("admin");var form=await admin.GetStringAsync("/AdminBinContributions/Lifecycle/1");
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync(path,Form(Token(form),("snapshot","ignored")))).StatusCode);
    }
    [Theory]
    [InlineData("","")] [InlineData("46.001","")] [InlineData("","15")] [InlineData("NaN","15")] [InlineData("Infinity","15")] [InlineData("91","15")] [InlineData("46","15")]
    public async Task MalformedLocationRejectsBeforeUpload(string latitude,string longitude)
    {
        using var user=Client("user");var html=await user.GetStringAsync("/BinContributions/Create/1");
        var response=await user.PostAsync("/BinContributions/Create",Form(Token(html),("BinId","1"),("RequestId",Guid.NewGuid().ToString()),("Type","Issue"),("Reason","WRONG_LOCATION"),("Latitude",latitude),("Longitude",longitude)));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(0,uploads.Count);
        await using var scope=app.Services.CreateAsyncScope();Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BinContributions.AnyAsync());
    }
    [Theory]
    [InlineData("image/svg+xml","photo.svg","<svg/>")] [InlineData("image/jpeg","fake.jpg","not JPEG")] [InlineData("image/png","empty.png","")]
    public async Task InvalidPhotosNeverPublish(string type,string filename,string body)
    {
        using var user=Client("user");var html=await user.GetStringAsync("/BinContributions/Create/1?photo=true");
        using var form=new MultipartFormDataContent();foreach(var pair in new[]{("__RequestVerificationToken",Token(html)),("BinId","1"),("Type","Photo"),("RequestId",Guid.NewGuid().ToString())})form.Add(new StringContent(pair.Item2),pair.Item1);
        var bytes=new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));bytes.Headers.ContentType=new(type);form.Add(bytes,"Photo",filename);
        Assert.Equal(HttpStatusCode.BadRequest,(await user.PostAsync("/BinContributions/Create",form)).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();Assert.Null((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.FindAsync(1))!.ImageUrl);
    }
    [Fact]
    public async Task RetiredExcludedFromHomeApiNearestAndStillVisibleToAdmin()
    {
        using var anon=Client();var home=await anon.GetStringAsync("/");Assert.Contains("OSM bin",home);Assert.DoesNotContain("Retired bin",home);Assert.Contains("Dodaj fotografijo",home);
        Assert.DoesNotContain("Retired bin",await anon.GetStringAsync("/api/trashbins/nearby"));Assert.DoesNotContain("Retired bin",await anon.GetStringAsync("/Map/FindNearest"));
        Assert.Contains("OSM bin",await anon.GetStringAsync("/Map/GetNearestBin?latitude=46&longitude=15.01"));
        Assert.Contains("OSM bin",await anon.GetStringAsync("/Map/GetBestBin?latitude=46&longitude=15.01"));
        using var admin=Client("admin");Assert.Contains("Retired bin",await admin.GetStringAsync("/AdminBins?state=retired"));
        var capture=Environment.GetEnvironmentVariable("DOGGYDROP_COMMUNITY_CAPTURE");if(capture!=null){Directory.CreateDirectory(capture);await File.WriteAllTextAsync(Path.Combine(capture,"home.html"),home);await File.WriteAllTextAsync(Path.Combine(capture,"contribute.html"),await admin.GetStringAsync("/BinContributions/Create/1"));await File.WriteAllTextAsync(Path.Combine(capture,"photo.html"),await admin.GetStringAsync("/BinContributions/Create/1?photo=true"));}
    }
    [Fact]
    public async Task SubmissionRateLimitAppliesOnServerAndReviewHasIndependentAccess()
    {
        using var user=Client("user");var html=await user.GetStringAsync("/BinContributions/Create/1");var token=Token(html);
        for(var i=0;i<10;i++)Assert.Equal(HttpStatusCode.Redirect,(await user.PostAsync("/BinContributions/Create",Form(token,("BinId","1"),("Type","Issue"),("Reason","BIN_MISSING"),("RequestId",Guid.NewGuid().ToString())))).StatusCode);
        Assert.Equal((HttpStatusCode)429,(await user.PostAsync("/BinContributions/Create",Form(token))).StatusCode);
        using var admin=Client("admin");var review=await admin.GetStringAsync("/AdminBinContributions/Review/1");
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminBinContributions/Review",Form(Token(review),("id","1"),("accept","false")))).StatusCode);
    }
    [Fact]
    public async Task PublicAddRemainsAnonymousButRequiresExplicitValidCoordinatesAndRejectsDuplicate()
    {
        using var anon=Client();var page=await anon.GetStringAsync("/Map/Add");var token=Token(page);
        var missing=await anon.PostAsync("/Map/Add",Form(token,("Name","Missing coordinates")));Assert.Equal(HttpStatusCode.OK,missing.StatusCode);
        var duplicate=await anon.PostAsync("/Map/Add",Form(token,("Name","Duplicate"),("Latitude","46"),("Longitude","15")));Assert.Contains("20 m",await duplicate.Content.ReadAsStringAsync());
        var valid=await anon.PostAsync("/Map/Add",Form(token,("Name","Public suggestion"),("Latitude","46.2"),("Longitude","15")));Assert.Equal(HttpStatusCode.Redirect,valid.StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var bin=await db.TrashBins.SingleAsync(b=>b.Name=="Public suggestion");Assert.False(bin.IsApproved);Assert.Null(bin.UserId);Assert.Null(bin.ImageUrl);
        Assert.DoesNotContain("useCurrentLocation(true)",page);
    }
    [Fact]
    public async Task ValidPhotoApprovalIsPrivateUntilAtomicAdminDecision()
    {
        using var user=Client("user");var page=await user.GetStringAsync("/BinContributions/Create/1?photo=true");
        using var bitmap=new SkiaSharp.SKBitmap(4,4);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        using var form=new MultipartFormDataContent();foreach(var pair in new[]{("__RequestVerificationToken",Token(page)),("BinId","1"),("Type","Photo"),("RequestId",Guid.NewGuid().ToString())})form.Add(new StringContent(pair.Item2),pair.Item1);
        var bytes=new ByteArrayContent(encoded.ToArray());bytes.Headers.ContentType=new("image/png");form.Add(bytes,"Photo","real.png");
        Assert.Equal(HttpStatusCode.Redirect,(await user.PostAsync("/BinContributions/Create",form)).StatusCode);
        const string asset="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp";
        using var anon=Client();Assert.DoesNotContain(asset,await anon.GetStringAsync("/"));Assert.DoesNotContain(asset,await user.GetStringAsync("/BinContributions/Mine"));
        using var admin=Client("admin");var review=await admin.GetStringAsync("/AdminBinContributions/Review/1");Assert.Contains(asset,review);
        var capture=Environment.GetEnvironmentVariable("DOGGYDROP_COMMUNITY_CAPTURE");if(capture!=null){Directory.CreateDirectory(capture);await File.WriteAllTextAsync(Path.Combine(capture,"admin-review.html"),review);await File.WriteAllTextAsync(Path.Combine(capture,"admin-queue.html"),await admin.GetStringAsync("/AdminBinContributions"));await File.WriteAllTextAsync(Path.Combine(capture,"admin-lifecycle.html"),await admin.GetStringAsync("/AdminBinContributions/Lifecycle/1"));}

        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminBinContributions/Review",Form(Token(review),("id","1"),("accept","true")))).StatusCode);
        Assert.Contains(asset,await anon.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsync("/AdminBinContributions/Review",Form(Token(review),("id","1"),("accept","true")))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var bin=(await db.TrashBins.FindAsync(1))!;Assert.Equal(1,bin.DataSourceId);Assert.Null(bin.UserId);Assert.Single(await db.UserNotifications.ToListAsync());

        // Continue the real OSM first-photo flow through correction, saved guidance, retirement and reactivation.
        async Task SubmitIssue(string reason, params (string, string)[] extra) {
            var page = await user.GetStringAsync("/BinContributions/Create/1");
            var fields = new List<(string, string)> { ("BinId", "1"), ("Type", "Issue"), ("Reason", reason), ("RequestId", Guid.NewGuid().ToString()) };
            fields.AddRange(extra);
            Assert.Equal(HttpStatusCode.Redirect, (await user.PostAsync("/BinContributions/Create", Form(Token(page), fields.ToArray()))).StatusCode);
        }
        async Task Accept(long id) {
            var page = await admin.GetStringAsync($"/AdminBinContributions/Review/{id}");
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/AdminBinContributions/Review", Form(Token(page), ("id", id.ToString()), ("accept", "true")))).StatusCode);
        }
        await SubmitIssue("WRONG_LOCATION", ("Latitude", "46.002"), ("Longitude", "15"));
        Assert.Equal(46, (await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == 1)).Latitude);
        await Accept(2);
        Assert.Equal(46.002, (await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == 1)).Latitude);
        var dog = new Dog { OwnerId = "user", Name = "Dog" };
        var plan = new PlannedWalk { OwnerId = "user", Dog = dog,
            Stops = [new() { Type = "bin", Name = "OSM bin", Latitude = 46.002, Longitude = 15 }],
            RoutePoints = [new() { Latitude = 46.002, Longitude = 15 }] };
        var history = new Walk { OwnerId = "user", Dog = dog, PlannedWalk = plan, Status = "Completed", DistanceMeters = 1234 };
        db.Walks.Add(history); await db.SaveChangesAsync();
        await SubmitIssue("BIN_MISSING"); await Accept(3);
        Assert.DoesNotContain("OSM bin", await anon.GetStringAsync("/"));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/Map/GetNearestBin?latitude=46.002&longitude=15")).StatusCode);
        Assert.False(await db.TrashBins.PublicBins().AnyAsync(b => b.Id == 1));
        var startToken = HomeToken(await user.GetStringAsync("/"));
        var blocked = await user.PostAsync("/Walks/Start", Form(startToken, ("dogId", dog.Id.ToString()), ("plannedWalkId", plan.Id.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode); Assert.Equal("/Walks", blocked.Headers.Location!.ToString());
        Assert.False(await db.Walks.AnyAsync(w => w.Status == "Active"));
        var saved = await db.PlannedWalks.AsNoTracking().Include(p => p.Stops).Include(p => p.RoutePoints).SingleAsync();
        Assert.Equal("OSM bin", Assert.Single(saved.Stops!).Name); Assert.Single(saved.RoutePoints!); Assert.Null(saved.UsedAt);
        var current = await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == 1);
        var lifecycle = await admin.GetStringAsync("/AdminBinContributions/Lifecycle/1");
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/AdminBinContributions/Lifecycle/1",
            Form(Token(lifecycle), ("snapshot", BinCommunityRules.Snapshot(current)), ("retired", "false")))).StatusCode);
        var started = await user.PostAsync("/Walks/Start", Form(startToken, ("dogId", dog.Id.ToString()), ("plannedWalkId", plan.Id.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode); Assert.StartsWith("/Walks/Active/", started.Headers.Location!.ToString());
        Assert.Single(await db.Walks.Where(w => w.Status == "Active").ToListAsync());
        current = await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == 1);
        Assert.Equal(1, current.DataSourceId); Assert.Null(current.UserId); Assert.Contains(asset, current.ImageUrl);
        Assert.Contains("OSM bin", await anon.GetStringAsync("/"));
        Assert.Equal(1234, (await db.Walks.AsNoTracking().SingleAsync(w => w.Id == history.Id)).DistanceMeters);
    }
    private static async Task CaptureUx(string name, string html)
    {
        var capture = Environment.GetEnvironmentVariable("DOGGYDROP_COMMUNITY_CAPTURE");
        if (capture == null) return;
        Directory.CreateDirectory(capture);
        await File.WriteAllTextAsync(Path.Combine(capture, name + ".html"), html);
    }

    [Fact]
    public async Task PublicContributionFormsKeepNativeUploadAndAssociatedValidation()
    {
        using var user = Client("user");
        var photo = await user.GetStringAsync("/BinContributions/Create/1?photo=true");
        Assert.Contains("for=\"contributionPhoto\"", photo);
        Assert.Contains("name=\"Photo\"", photo);
        Assert.Contains("accept=\"image/jpeg,image/png,image/webp\"", photo);
        Assert.DoesNotContain("capture=", photo);
        Assert.Contains("aria-describedby=\"photoHelp photoError\"", photo);
        await CaptureUx("photo", photo);
        var issue = await user.GetStringAsync("/BinContributions/Create/1");
        Assert.Contains("community-source-note", issue);
        Assert.Contains("Popravek v DoggyDrop ne spreminja OpenStreetMap.", issue);
        Assert.DoesNotContain("PRIVATE SOURCE", issue);
        await CaptureUx("contribute", issue);
        var invalid = await user.PostAsync("/BinContributions/Create", Form(Token(issue), ("BinId", "1"),
            ("Type", "Issue"), ("Description", new string('x', 1001)), ("RequestId", Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("id=\"descriptionError\"", errors);
        Assert.Contains("aria-invalid=\"true\"", errors);
        await CaptureUx("contribute-errors", errors);
        var missingPhoto = await user.PostAsync("/BinContributions/Create", Form(Token(photo), ("BinId", "1"),
            ("Type", "Photo"), ("RequestId", Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.BadRequest, missingPhoto.StatusCode);
        var photoErrors = await missingPhoto.Content.ReadAsStringAsync();
        Assert.Contains("validation-summary-errors", photoErrors);
        await CaptureUx("photo-errors", photoErrors);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BinContributions.ToListAsync());
    }

    [Fact]
    public async Task HistoryCardsUseEncodedNamesAndSafePublicMapLinksWithoutPrivateMetadata()
    {
        using var user = Client("user");
        await CaptureUx("mine-empty", await user.GetStringAsync("/BinContributions/Mine"));
        await using (var scope = app.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.TrashBins.FindAsync(1))!.Name = "Park ob reki <img src=x onerror=alert(1)>";
            db.BinContributions.Add(new BinContribution { BinId=1, SubmittedByUserId="user", Type=BinContributionType.Photo,
                ReviewNote="PRIVATE REVIEW", ProposedPhotoUrl="/uploads/PRIVATE_PHOTO.webp", RequestId=Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var single = await user.GetStringAsync("/BinContributions/Mine");
        Assert.Contains("Park ob reki &lt;img", single);
        Assert.DoesNotContain("Park ob reki <img", single);
        Assert.Contains("href=\"/?binId=1\"", single);
        Assert.DoesNotContain("PRIVATE", single);
        await CaptureUx("mine-one", single);
        await using (var scope = app.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BinContributions.AddRange(
                new BinContribution { BinId=2, SubmittedByUserId="user", Type=BinContributionType.Issue, Reason=BinIssueReason.WRONG_LOCATION, Status=BinContributionStatus.Approved, RequestId=Guid.NewGuid() },
                new BinContribution { BinId=3, SubmittedByUserId="user", Type=BinContributionType.Issue, Reason=BinIssueReason.DAMAGED, Status=BinContributionStatus.Rejected, RequestId=Guid.NewGuid() },
                new BinContribution { BinId=1, SubmittedByUserId="admin", Type=BinContributionType.Photo, RequestId=Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var multiple = await user.GetStringAsync("/BinContributions/Mine");
        Assert.Equal(3, Regex.Matches(multiple, "<article>").Count);
        Assert.DoesNotContain("href=\"/?binId=2\"", multiple);
        Assert.DoesNotContain("href=\"/?binId=3\"", multiple);
        foreach (var status in new[] { "V pregledu", "Odobreno", "Zavrnjeno" }) Assert.Contains(status, multiple);
        await CaptureUx("mine-multiple", multiple);
    }

    [Fact]
    public async Task SubmissionSuccessRemainsReviewPendingAndEmptyHistoryOffersMap()
    {
        using var user = Client("user");
        var empty = WebUtility.HtmlDecode(await user.GetStringAsync("/BinContributions/Mine"));
        Assert.Contains("Tvoj prvi prispevek šteje", empty);
        Assert.Contains("href=\"/\"", empty);
        var form = await user.GetStringAsync("/BinContributions/Create/1");
        var response = await user.PostAsync("/BinContributions/Create", Form(Token(form), ("BinId", "1"),
            ("Type", "Issue"), ("Reason", "DAMAGED"), ("RequestId", Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var success = await user.GetStringAsync(response.Headers.Location!);
        Assert.Contains("community-success\" role=\"status\"", success);
        Assert.Contains("Hvala. Prijavo bomo pregledali.", success);
        await CaptureUx("mine-success", success);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Equal(BinContributionStatus.Pending, (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BinContributions.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedSubmissionReturnsEnabledFormWithTextAndSameIdempotencyKey(bool photo)
    {
        using var user = Client("user");
        var page = await user.GetStringAsync("/BinContributions/Create/1?photo=" + photo);
        var requestId = Guid.NewGuid().ToString();
        var result = await user.PostAsync("/BinContributions/Create", Form(Token(page), ("BinId", "1"),
            ("Type", photo ? "Photo" : "Issue"), ("RequestId", requestId), ("Description", "Opis za ponovni poskus")));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        var html = await result.Content.ReadAsStringAsync();
        Assert.Contains("Opis za ponovni poskus", html);
        Assert.Contains(requestId, html);
        Assert.Contains("aria-busy=\"false\"", html);
        var button = Regex.Match(html, "<button[^>]*id=\"contributionSubmit\"[^>]*>").Value;
        Assert.NotEmpty(button); Assert.DoesNotContain("disabled", button);
        Assert.Contains("validation-summary-errors", html);
        await CaptureUx(photo ? "submit-photo-errors" : "submit-report-errors", html);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().BinContributions.ToListAsync());
    }

    public async Task DisposeAsync(){if(app!=null)await app.DisposeAsync();File.Delete(file);}
    private sealed class Uploads:ICloudinaryService,IBinPhotoStorage
    {
        public int Count;
        public async Task<string?> UploadTrashBinImageAsync(IFormFile f){Count++;using var stream=f.OpenReadStream();var image=await new ImageOptimizationService().OptimizeAsync(stream,f.ContentType,f.FileName,ImageOptimizationPreset.TrashBin);await using var content=image.Content;return image.WasOptimized?"/uploads/trashbins/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp":null;}
        public Task<string?> UploadImageAsync(IFormFile f)=>throw new NotSupportedException();public Task<string?> UploadWalkImageAsync(IFormFile f)=>throw new NotSupportedException();public bool CanRotate(string? u)=>false;public Task<string?> RotateCopyAsync(string u,int d)=>throw new NotSupportedException();public Task DeleteManagedAsync(string u)=>Task.CompletedTask;
    }
    private sealed class UserAuth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var id=Request.Headers["X-Test-User"].ToString();if(id is not("user" or "admin"))return Task.FromResult(AuthenticateResult.NoResult());var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,id),new Claim(ClaimTypes.Name,id)],IdentityConstants.ApplicationScheme);if(id=="admin")identity.AddClaim(new Claim(ClaimTypes.Role,"Admin"));return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));}
    }
}
