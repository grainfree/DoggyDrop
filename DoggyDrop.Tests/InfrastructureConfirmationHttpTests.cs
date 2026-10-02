using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Antiforgery;
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

public sealed class InfrastructureConfirmationHttpTests:IAsyncLifetime
{
    private WebApplication app=null!;
    private readonly string file=Path.Combine(Path.GetTempPath(),$"doggydrop-confirmation-{Guid.NewGuid():N}.db");
    public async Task InitializeAsync(){
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!Directory.Exists(Path.Combine(root.FullName,"DoggyDrop","Views")))root=root.Parent;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(ConfirmationsController).Assembly.GetName().Name,
            ContentRootPath=Path.Combine(root!.FullName,"DoggyDrop"),EnvironmentName="Production",Args=[]});
        builder.Configuration.Sources.Clear();builder.Configuration.AddInMemoryCollection();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={file};Pooling=False"));
        builder.Services.AddSingleton<TimeProvider>(new InfrastructureConfirmationTests.Clock());
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddAntiforgery(o=>o.HeaderName="RequestVerificationToken");
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test";})
            .AddScheme<AuthenticationSchemeOptions,TestUser>("Test",_=>{});
        builder.Services.AddAuthorization();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ConfirmationsController).Assembly);
        builder.Services.AddRateLimiter(o=>o.RejectionStatusCode=429);builder.Services.AddCommunityConfirmations();
        app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();
        app.MapGet("/test-token",(Microsoft.AspNetCore.Http.HttpContext context,IAntiforgery antiforgery)=>antiforgery.GetAndStoreTokens(context).RequestToken!);
        app.MapControllers();app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");
        await using(var scope=app.Services.CreateAsyncScope()){var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser{Id="a"},new ApplicationUser{Id="b"},new ApplicationUser{Id="admin"});
            db.TrashBins.AddRange(new TrashBin{Id=1,Name="Koš",Latitude=46,Longitude=15,IsApproved=true},new TrashBin{Id=2,Name="Pending",Latitude=46,Longitude=15},new TrashBin{Id=3,Name="Retired",Latitude=46,Longitude=15,IsApproved=true,IsRetired=true});
            db.WaterPoints.AddRange(new WaterPoint{Id=1,Latitude=46,Longitude=15,IsApproved=true,Potability=WaterPotability.SourceReportedDrinking},new WaterPoint{Id=2,Latitude=46,Longitude=15},new WaterPoint{Id=3,Latitude=46,Longitude=15,IsApproved=true,IsRetired=true,Potability=WaterPotability.SourceReportedDrinking});
            await db.SaveChangesAsync();}await app.StartAsync();
    }
    private HttpClient Client(string? user="a"){var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};if(user!=null)client.DefaultRequestHeaders.Add("X-Test-User",user);return client;}
    private async Task<HttpClient> Ready(string? user="a"){var c=Client(user);c.DefaultRequestHeaders.Add("RequestVerificationToken",await c.GetStringAsync("/test-token"));return c;}
    private async Task<int> Count(){await using var scope=app.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InfrastructureConfirmations.CountAsync();}
    private const string Valid="{\"latitude\":46,\"longitude\":15,\"accuracy\":5}";
    private static Task<HttpResponseMessage> Send(HttpClient c,string kind="bin",int id=1,string json=Valid)=>c.PostAsync($"/api/confirmations/{kind}/{id}",new StringContent(json,Encoding.UTF8,"application/json"));
    [Theory][InlineData("bin")][InlineData("water")]
    public async Task AcceptedResponseIsOnlyPublicSummaryAndReplayIsSafe(string kind){using var c=await Ready();var response=await Send(c,kind);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<ConfirmationResult>();Assert.Equal("accepted",result!.Outcome);Assert.Equal(1,result.Summary!.RecentUniqueConfirmers);
        var json=await response.Content.ReadAsStringAsync();foreach(var secret in new[]{"userId","latitude","longitude","accuracy","evidenceVersion","requestId"})Assert.DoesNotContain(secret,json);
        Assert.Contains("no-store",response.Headers.CacheControl!.ToString());Assert.Equal("cooldown",(await (await Send(c,kind)).Content.ReadFromJsonAsync<ConfirmationResult>())!.Outcome);Assert.Equal(1,await Count());}
    [Theory][InlineData("{}")] [InlineData("null")] [InlineData("{\"latitude\":46,\"longitude\":15}")]
    [InlineData("{\"longitude\":15,\"accuracy\":5}")][InlineData("{\"latitude\":46,\"accuracy\":5}")]
    [InlineData("{\"latitude\":91,\"longitude\":15,\"accuracy\":5}")][InlineData("{\"latitude\":46,\"longitude\":181,\"accuracy\":5}")]
    [InlineData("{\"latitude\":46,\"longitude\":15,\"accuracy\":-1}")][InlineData("{\"latitude\":\"NaN\",\"longitude\":15,\"accuracy\":5}")]
    [InlineData("{\"latitude\":46,\"longitude\":\"Infinity\",\"accuracy\":5}")]
    public async Task MalformedInputsNeverWrite(string json){using var c=await Ready();Assert.Equal(HttpStatusCode.BadRequest,(await Send(c,json:json)).StatusCode);Assert.Equal(0,await Count());}
    [Theory][InlineData("bin",2)][InlineData("bin",3)][InlineData("bin",999)][InlineData("water",2)][InlineData("water",3)][InlineData("water",999)]
    public async Task HiddenTargetsRejected(string kind,int id){using var c=await Ready();Assert.Equal(HttpStatusCode.Gone,(await Send(c,kind,id)).StatusCode);Assert.Equal(0,await Count());}
    [Theory][InlineData(null)][InlineData("")][InlineData("invalid")]
    public async Task AuthenticationAndAntiforgery(string? mode){using var c=Client(mode==null?null:"a");if(mode=="invalid")c.DefaultRequestHeaders.Add("RequestVerificationToken","invalid");
        Assert.Equal(mode==null?HttpStatusCode.Unauthorized:HttpStatusCode.BadRequest,(await Send(c)).StatusCode);Assert.Equal(0,await Count());}
    [Fact]public async Task JsonOnlyBodyLimitAndMassAssignment(){using var c=await Ready();Assert.Equal(HttpStatusCode.UnsupportedMediaType,(await c.PostAsync("/api/confirmations/bin/1",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,(await Send(c,json:"{\"extra\":\""+new string('x',3000)+"\"}")).StatusCode);
        var r=await Send(c,json:Valid[..^1]+",\"userId\":\"b\",\"createdAt\":\"2000-01-01\",\"proximityVerified\":true,\"type\":2}");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var row=await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InfrastructureConfirmations.SingleAsync();Assert.Equal("a",row.UserId);Assert.Equal(2026,row.CreatedAt.Year);Assert.Equal(InfrastructureConfirmationType.TrashBinPresent,row.Type);}
    [Fact]public async Task RateLimitIsPerAccountAndBoundsAttempts(){using var a=await Ready();for(var i=0;i<20;i++)Assert.Equal(HttpStatusCode.BadRequest,(await Send(a,json:"{}")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await Send(a)).StatusCode);using var b=await Ready("b");Assert.Equal(HttpStatusCode.OK,(await Send(b)).StatusCode);Assert.Equal(1,await Count());}
    [Theory][InlineData(null,HttpStatusCode.Unauthorized)][InlineData("a",HttpStatusCode.Forbidden)]
    public async Task AdminHistoryAuthorization(string? user,HttpStatusCode expected){using var c=Client(user);Assert.Equal(expected,(await c.GetAsync("/AdminConfirmations?kind=bin&id=1")).StatusCode);}
    [Fact]public async Task AdminHistoryIsReadOnlyAndDoesNotExposeIdentityOrLocationEvidence(){
        using var user=await Ready();await Send(user);using var admin=Client("admin");
        var response=await admin.GetAsync("/AdminConfirmations?kind=bin&id=1");Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();Assert.Contains("Vseh opažanj: 1",html);
        foreach(var field in new[]{"UserId","EvidenceVersion","latitude","longitude","accuracy"})Assert.DoesNotContain(field,html);
        Assert.Equal(1,await Count());var capture=Environment.GetEnvironmentVariable("DOGGYDROP_CONFIRMATION_CAPTURE");
        if(!string.IsNullOrEmpty(capture)){Directory.CreateDirectory(capture);await File.WriteAllTextAsync(Path.Combine(capture,"admin-confirmations.html"),html);}
    }
    public async Task DisposeAsync(){await app.StopAsync();await app.DisposeAsync();File.Delete(file);}
    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder){
        protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var id=Request.Headers["X-Test-User"].ToString();if(string.IsNullOrEmpty(id))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,id)};if(id=="admin")claims.Add(new(ClaimTypes.Role,"Admin"));return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,"Test")),"Test")));}
    }
}
