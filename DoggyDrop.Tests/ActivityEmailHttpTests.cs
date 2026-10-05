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

public sealed class ActivityEmailHttpTests : IAsyncLifetime
{
    private readonly string file=Path.Combine(Path.GetTempPath(),"email-http-"+Guid.NewGuid()+".db");
    private WebApplication app=null!;
    public async Task InitializeAsync() {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj")))root=root.Parent;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(NotificationPreferencesController).Assembly.GetName().Name,ContentRootPath=Path.Combine(root!.FullName,"DoggyDrop"),EnvironmentName="Testing",Args=[]});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.example";
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={file};Pooling=False"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test";o.DefaultForbidScheme="Test";}).AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});
        builder.Services.AddSingleton(TimeProvider.System);builder.Services.AddControllersWithViews().AddApplicationPart(typeof(NotificationPreferencesController).Assembly);
        builder.Services.AddRazorPages();app=builder.Build();app.UseMiddleware<LaunchHeaders>();app.UseRouting();app.UseAuthentication();app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await With(async db=>{await db.Database.EnsureCreatedAsync();await ActivityEmailTests.Seed(db);db.Users.Add(new(){Id="admin",UserName="admin"});await db.SaveChangesAsync();await ActivityEmailTests.Queue(db);await db.NotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.Status,EmailDeliveryStatus.Failed).SetProperty(n=>n.AttemptCount,5));});
        await app.StartAsync();
    }
    private async Task With(Func<ApplicationDbContext,Task> action){await using var scope=app.Services.CreateAsyncScope();await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());}
    private HttpClient Client(string? user=null){var c=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};if(user!=null)c.DefaultRequestHeaders.Add("X-Test-User",user);return c;}
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string,string)[] pairs)=>new(pairs.Select(x=>new KeyValuePair<string,string>(x.Item1,x.Item2)));
    [Theory][InlineData(null,401)][InlineData("a",403)]
    public async Task AdminListAndRetryDenyNonAdmins(string? who,int expected){using var client=Client(who);Assert.Equal(expected,(int)(await client.GetAsync("/AdminEmail")).StatusCode);var result=await client.PostAsync("/AdminEmail/Retry",Form(("id","1")));Assert.Equal(expected,(int)result.StatusCode);}
    [Fact] public async Task AnonymousPreferencesDenied(){using var c=Client();Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/NotificationPreferences")).StatusCode);}
    [Theory][InlineData("")][InlineData("bad-token")]
    public async Task PreferenceCsrfRequired(string token){using var c=Client("a");Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/NotificationPreferences",Form(("contributionUpdates","false"),("__RequestVerificationToken",token)))).StatusCode);await With(async db=>Assert.Empty(await db.NotificationPreferences.ToListAsync()));}
    [Theory][InlineData("UserId","b")][InlineData("email","b@example.test")][InlineData("Marketing","true")][InlineData("category","security")][InlineData("contributionUpdates","invalid")]
    public async Task UnknownOrMalformedPreferenceCannotMutateOtherData(string key,string value){using var c=Client("a");var token=Token(await c.GetStringAsync("/NotificationPreferences"));var fields=new List<(string,string)>{("__RequestVerificationToken",token)};if(key!="contributionUpdates")fields.Add(("contributionUpdates","false"));fields.Add((key,value));Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/NotificationPreferences",Form(fields.ToArray()))).StatusCode);await With(async db=>Assert.Empty(await db.NotificationPreferences.ToListAsync()));}
    [Fact] public async Task OwnPreferenceSaveWorksAndQueryCannotSelectAnotherUser(){using var c=Client("a");var html=await c.GetStringAsync("/NotificationPreferences?userId=b");Assert.Contains("checked",html);var response=await c.PostAsync("/NotificationPreferences?userId=b",Form(("contributionUpdates","false"),("__RequestVerificationToken",Token(html))));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);var saved=await c.GetStringAsync(response.Headers.Location);Assert.Contains("role=\"status\"",saved);await With(async db=>{var row=await db.NotificationPreferences.SingleAsync();Assert.Equal("a",row.UserId);Assert.False(row.ContributionUpdates);});Capture("preferences-saved",saved);}
    [Theory][InlineData("")][InlineData("bad-token")]
    public async Task RetryCsrfRequired(string token){using var c=Client("admin");Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/AdminEmail/Retry",Form(("id","1"),("__RequestVerificationToken",token)))).StatusCode);}
    [Fact] public async Task RetryIsSingleLogicalWorkWithoutArbitraryRecipient(){using var c=Client("admin");var html=await c.GetStringAsync("/AdminEmail");var token=Token(html);Assert.DoesNotContain("a@example.test",html);Assert.DoesNotContain("LeaseToken",html);Assert.DoesNotContain("EventKey",html);Assert.Contains("noindex",html);Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/AdminEmail/Retry",Form(("id","1"),("recipient","attacker@example.test"),("__RequestVerificationToken",token)))).StatusCode);var fields=Form(("id","1"),("__RequestVerificationToken",token));Assert.Equal(HttpStatusCode.Redirect,(await c.PostAsync("/AdminEmail/Retry",fields)).StatusCode);Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsync("/AdminEmail/Retry",Form(("id","1"),("__RequestVerificationToken",token)))).StatusCode);await With(async db=>{var row=await db.NotificationOutbox.SingleAsync();Assert.Equal(5,row.AttemptCount);Assert.Equal(6,row.AttemptLimit);Assert.Equal("a",row.RecipientUserId);});Capture("admin-email",html);}
    [Theory][InlineData(EmailDeliveryStatus.Sent)][InlineData(EmailDeliveryStatus.Pending)][InlineData(EmailDeliveryStatus.Processing)][InlineData(EmailDeliveryStatus.Suppressed)]
    public async Task NonFailedCannotRetry(EmailDeliveryStatus status){await With(db=>db.NotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.Status,status)));using var c=Client("admin");var token=Token(await c.GetStringAsync("/AdminEmail"));if(token=="")token=Token(await c.GetStringAsync("/NotificationPreferences"));Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsync("/AdminEmail/Retry",Form(("id","1"),("__RequestVerificationToken",token)))).StatusCode);}
    [Theory][InlineData("/test-email")][InlineData("/EmailTest/SendTestEmail")][InlineData("/Home/TestEmail")]
    public async Task LegacyEmailTestEndpointsAreGone(string path){using var c=Client("admin");Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync(path)).StatusCode);}
    [Theory][InlineData("/AdminEmail?status=999")][InlineData("/AdminEmail?type=999")][InlineData("/AdminEmail?page=0")]
    public async Task AdminFiltersValidate(string path){using var c=Client("admin");Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(path)).StatusCode);}
    [Fact] public async Task PreferencePagesArePrivateAndAccessible(){using var c=Client("a");c.DefaultRequestHeaders.Host="hostile.example";var r=await c.GetAsync("/NotificationPreferences");var html=await r.Content.ReadAsStringAsync();Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(r.Headers.CacheControl!.NoStore);Assert.True(r.Headers.CacheControl.Private);Assert.Contains("aria-describedby=\"email-help\"",html);Assert.DoesNotContain("hostile.example",html);Capture("preferences-default",html);}
    private static void Capture(string name,string html){var path=Environment.GetEnvironmentVariable("DOGGYDROP_EMAIL_CAPTURE");if(string.IsNullOrEmpty(path))return;Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,name+".html"),html);}
    public async Task DisposeAsync(){await app.DisposeAsync();File.Delete(file);}
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory log,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,log,encoder){protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var id=Request.Headers["X-Test-User"].ToString();if(id=="")return Task.FromResult(AuthenticateResult.NoResult());var claims=new List<Claim>{new(ClaimTypes.NameIdentifier,id),new(ClaimTypes.Name,id)};if(id=="admin")claims.Add(new(ClaimTypes.Role,"Admin"));return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,IdentityConstants.ApplicationScheme)),Scheme.Name)));}protected override Task HandleChallengeAsync(AuthenticationProperties properties){Response.StatusCode=401;return Task.CompletedTask;}protected override Task HandleForbiddenAsync(AuthenticationProperties properties){Response.StatusCode=403;return Task.CompletedTask;}}
}
