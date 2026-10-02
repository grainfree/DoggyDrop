using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WaterPointHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-import-http-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    private readonly FakeRoutes routes=new();
    private readonly ImportClock clock = new();
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj"))) root=root.Parent;
        Assert.NotNull(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(PlacesController).Assembly.GetName().Name,
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"),EnvironmentName="Production",Args=[] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddSingleton<IGamificationCalendar,GamificationCalendar>();
        builder.Services.AddScoped<IGamificationService,GamificationService>();
        builder.Services.AddScoped<IWeeklyGoalsService,WeeklyGoalsService>();
        builder.Services.AddScoped<INotificationService,NotificationService>();
        builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<WaterImportSessions>();builder.Services.AddScoped<WaterImportService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=> { o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test"; })
            .AddScheme<AuthenticationSchemeOptions,TestUser>("Test",_=>{});
        builder.Services.AddWalkingRouting();builder.Services.AddSingleton<IWalkingRoutes>(routes);
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PlacesController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(sp => new MapController(sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IWebHostEnvironment>(),sp.GetRequiredService<UserManager<ApplicationUser>>(),null!,null!,null!,
            sp.GetRequiredService<IGamificationService>(),null!,null!,null!,sp.GetRequiredService<IGamificationCalendar>(),null!,placeLogoCloud:new("test")));
        builder.Services.AddRazorPages().AddApplicationPart(typeof(PlacesController).Assembly);
        app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
        app.MapControllers();app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id="admin",UserName="Admin" },new ApplicationUser { Id="other",UserName="Other" },new ApplicationUser { Id="user",UserName="User" });
            db.DataSources.Add(new DataSource { Name="Občina ČŠŽ",Type=DataSourceType.Municipality,DataDate=new DateOnly(2026,8,31) });await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user=null)
    {
        var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(app.Urls.Single()) };
        if(user!=null)client.DefaultRequestHeaders.Add("X-Test-User",user);return client;
    }
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,$"name=\"{name}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<string> Page(HttpClient client,string path)
    { var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadAsStringAsync(); }
    private static FormUrlEncodedContent Form(string token,params (string Key,string Value)[] fields)=>new(
        fields.Prepend(("__RequestVerificationToken",token)).Select(x=>new KeyValuePair<string,string>(x.Item1,x.Item2)));
    private static async Task<string> Upload(HttpClient client,string token,string csv,int sourceId=1,string filename="places.csv", string? category="PetShop")
    {
        using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");
        if(category!=null)form.Add(new StringContent(category),"category");
        form.Add(new StringContent(sourceId.ToString()),"sourceId");form.Add(new StringContent(";"),"delimiter");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),"file",filename);
        var response=await client.PostAsync("/AdminWaterImport/Upload",form);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        return response.Headers.Location!.OriginalString.Split('/').Last();
    }
    private static async Task<string> Map(HttpClient client,string token,string id)
    {
        var response=await client.PostAsync($"/AdminWaterImport/Map/{id}",Form(token,("version","0"),("name","0"),("latitude","1"),("longitude","2")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);return await Page(client,$"/AdminWaterImport/Preview/{id}");
    }

    private sealed class FakeRoutes:IWalkingRoutes{
        public int Calls;public WalkingCoordinate? Target;
        public Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points,CancellationToken ct=default){Calls++;Target=points[^1];return Task.FromResult(new WalkingRouteResult(points,100,90));}
    }
    [Fact]public async Task AuthorizationAndAntiforgeryEveryNewMutation(){
        using var anon=Client();using var user=Client("user");using var admin=Client("admin");
        foreach(var path in new[]{"AdminWaterPoints","AdminWaterPoints/Create","AdminWaterPoints/Edit/1","AdminWaterImport","AdminWaterImport/Map/id","AdminWaterImport/Preview/id","AdminWaterImport/Confirm/id","AdminWaterImport/Result/id"}){
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("/"+path)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await user.GetAsync("/"+path)).StatusCode);}
        foreach(var path in new[]{"AdminWaterPoints/Create","AdminWaterPoints/Edit/1","AdminWaterPoints/Retire/1","AdminWaterImport/Upload","AdminWaterImport/Map","AdminWaterImport/Select","AdminWaterImport/Prepare","AdminWaterImport/Apply","AdminWaterImport/Refresh","AdminWaterImport/Cancel"}){
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync("/"+path,Form(""))).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync("/"+path,Form(""))).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/"+path,Form(""))).StatusCode);}
    }
    [Fact]public async Task PreviewOwnershipXssAtomicConfirmationReplayAndCaptures(){
        using var admin=Client("admin");using var other=Client("other");var index=await Page(admin,"/AdminWaterImport");var token=Field(index,"__RequestVerificationToken");
        var id=await Upload(admin,token,"Name;Latitude;Longitude;Access\n<script>alert(1)</script>;46;15;Public\n;47;16;Unknown");
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/AdminWaterImport/Map/{id}")).StatusCode);
        var preview=await Map(admin,token,id);Assert.Contains("&lt;script&gt;",preview);Assert.DoesNotContain("<script>alert(1)</script>",preview);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(0,await db.WaterPoints.CountAsync());
        await Capture("import-index",index);await Capture("import-preview",preview);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminWaterImport/Prepare/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminWaterImport/Apply/{id}",Form(token,("version","0")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminWaterImport/Apply/{id}",Form(token,("version","1"), ("latitude","0")))).StatusCode);
        Assert.Equal(2,await db.WaterPoints.CountAsync());Assert.Equal(46,(await db.WaterPoints.OrderBy(p=>p.Id).FirstAsync()).Latitude);
        await admin.PostAsync($"/AdminWaterImport/Apply/{id}",Form(token,("version","1")));Assert.Equal(2,await db.WaterPoints.CountAsync());
        await Capture("admin-list",await Page(admin,"/AdminWaterPoints"));await Capture("admin-create",await Page(admin,"/AdminWaterPoints/Create"));await Capture("admin-edit",await Page(admin,"/AdminWaterPoints/Edit/1"));
    }
    [Fact]public async Task RenderedEditTokenPreservesMicrosecondsAndAllowsNormalEdit(){
        using var admin=Client("admin");await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]);
        var html=await Page(admin,"/AdminWaterPoints/Edit/1");var token=Field(html,"__RequestVerificationToken");var version=Field(html,"OriginalUpdatedAt");
        Assert.Equal((await db.WaterPoints.AsNoTracking().SingleAsync()).UpdatedAt.ToString("O"),version);
        var response=await admin.PostAsync("/AdminWaterPoints/Edit/1",Form(token,("OriginalUpdatedAt",version),("Name","Edited"),("Latitude","46"),("Longitude","15"),("DataSourceId","1"),("Potability","1"),("IsApproved","true")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);var point=await db.WaterPoints.AsNoTracking().SingleAsync();Assert.Equal("Edited",point.Name);Assert.Equal(1,point.DataSourceId);
    }
    [Fact]public async Task RetireReactivatePreserveProvenanceAndStaleFormFails(){
        using var admin=Client("admin");await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]);var first=await db.WaterPoints.AsNoTracking().SingleAsync();
        var token=Field(await Page(admin,"/AdminWaterPoints/Edit/1"),"__RequestVerificationToken");
        var original=first.UpdatedAt.ToString("O");Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminWaterPoints/Retire/1",Form(token,("originalUpdatedAt",original),("retired","true")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await admin.PostAsync("/AdminWaterPoints/Edit/1",Form(token,("OriginalUpdatedAt",original),("Latitude","46"),("Longitude","15")))).StatusCode);
        var retired=await db.WaterPoints.AsNoTracking().SingleAsync();Assert.True(retired.IsRetired);Assert.Empty(await WaterPoints.LoadAsync(db.WaterPoints));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminWaterPoints/Retire/1",Form(token,("originalUpdatedAt",retired.UpdatedAt.ToString("O")),("retired","false")))).StatusCode);
        var current=await db.WaterPoints.AsNoTracking().SingleAsync();Assert.Equal(first.Id,current.Id);Assert.Equal(first.DataSourceId,current.DataSourceId);Assert.Equal(first.DateAdded,current.DateAdded);Assert.Equal(first.ApprovedAt,current.ApprovedAt);Assert.Single(await WaterPoints.LoadAsync(db.WaterPoints));
    }
    [Fact]public async Task PublicRouteRevalidatesCurrentTargetUsesServerCoordinatesAndSharesRouter(){
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]);
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminWaterPoints/Create"),"__RequestVerificationToken");using var anon=Client();
        // This case exercises target revalidation; the public Home-token flow is covered separately below.
        var publicResponse=await anon.GetAsync("/api/waterpoints");Assert.Equal(HttpStatusCode.OK,publicResponse.StatusCode);Assert.True(publicResponse.Headers.CacheControl!.NoStore);
        var json=await publicResponse.Content.ReadAsStringAsync();Assert.DoesNotContain("UpdatedAt",json);Assert.DoesNotContain("Contact",json);
        admin.DefaultRequestHeaders.Add("RequestVerificationToken",token);
        var response=await admin.PostAsync("/api/waterpoints/1/route",new StringContent("{\"origin\":{\"latitude\":46.1,\"longitude\":15.1},\"destination\":{\"latitude\":0,\"longitude\":0}}",Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(1,routes.Calls);Assert.Equal(new WalkingCoordinate(46,15),routes.Target);
        await db.WaterPoints.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.IsRetired,true));
        response=await admin.PostAsync("/api/waterpoints/1/route",new StringContent("{\"origin\":{\"latitude\":46.1,\"longitude\":15.1}}",Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.Gone,response.StatusCode);Assert.Equal(1,routes.Calls);Assert.Equal("[]",await anon.GetStringAsync("/api/waterpoints"));
    }
    [Theory][InlineData(null)][InlineData("user")]
    public async Task PublicHomeTokenAllowsWaterRoutingAndRejectsMissingOrInvalidAntiforgery(string? user){
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]);
        // Same client/cookie jar, real anonymous or ordinary-user Home and the exact token used by its JS.
        using var client=Client(user);var home=await client.GetAsync("/");Assert.Equal(HttpStatusCode.OK,home.StatusCode);
        Assert.Contains(home.Headers.GetValues("Set-Cookie"),value=>value.Contains("Antiforgery"));
        var html=await home.Content.ReadAsStringAsync();
        var token=JsonSerializer.Deserialize<string>(Regex.Match(html,"const requestVerificationToken = ([^;]+);").Groups[1].Value);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Task<HttpResponseMessage> Route()=>client.PostAsync("/api/waterpoints/1/route",new StringContent("{\"origin\":{\"latitude\":46.1,\"longitude\":15.1}}",Encoding.UTF8,"application/json"));
        Assert.Equal(HttpStatusCode.BadRequest,(await Route()).StatusCode);Assert.Equal(0,routes.Calls);
        client.DefaultRequestHeaders.Add("RequestVerificationToken","invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest,(await Route()).StatusCode);Assert.Equal(0,routes.Calls);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");client.DefaultRequestHeaders.Add("RequestVerificationToken",token);
        Assert.Equal(HttpStatusCode.OK,(await Route()).StatusCode);Assert.Equal(1,routes.Calls);
        Assert.Equal(new WalkingCoordinate(46,15),routes.Target);
    }
    [Theory][InlineData("{}")] [InlineData("{\"origin\":{}}")][InlineData("{\"origin\":{\"latitude\":46}}")][InlineData("{\"origin\":{\"longitude\":15}}")]
    [InlineData("{\"origin\":null}")][InlineData("{\"origin\":{\"latitude\":91,\"longitude\":15}}")]
    public async Task InvalidRouteInputConsumesNoProviderAttempt(string body){
        using var admin=Client("admin");admin.DefaultRequestHeaders.Add("RequestVerificationToken",Field(await Page(admin,"/AdminWaterPoints/Create"),"__RequestVerificationToken"));
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/api/waterpoints/1/route",new StringContent(body,Encoding.UTF8,"application/json"))).StatusCode);Assert.Equal(0,routes.Calls);
    }
    private static async Task Capture(string name,string html)
    {
        var folder=Environment.GetEnvironmentVariable("DOGGYDROP_WATER_CAPTURE");
        if(!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,name+".html"),html); }
    }
    public async Task DisposeAsync() {if(app!=null)await app.DisposeAsync();if(File.Exists(database))File.Delete(database);}
    private sealed class ImportClock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user=Request.Headers["X-Test-User"].ToString();if(user is not ("admin" or "other" or "user"))return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)],IdentityConstants.ApplicationScheme);
            if(user!="user")identity.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
}
