using System.Net;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DoggyDrop.Tests;

// Actual Razor/Identity/antiforgery/relational store; ONLY synthetic provider cookies.
// Application Program, provider backchannels, email, bootstrap and migrations never run.
public sealed class ExternalLoginSecurityTests : IAsyncLifetime
{
    const string External = "/Identity/Account/ExternalLogin";
    const string Manage = "/Identity/Account/Manage/ExternalLogins";
    readonly string database = Path.Combine(Path.GetTempPath(), $"external-login-{Guid.NewGuid():N}.db");
    readonly string password = "Synthetic-" + Guid.NewGuid().ToString("N") + "!aA7";
    readonly FailLoginInsert failure = new();
    WebApplication app = null!;
    public async Task InitializeAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DoggyDrop.sln"))) dir = dir.Parent;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(HomeController).Assembly.GetName().Name, EnvironmentName = "Production",
            ContentRootPath = Path.Combine(dir!.FullName, "DoggyDrop"), Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database};Pooling=False").AddInterceptors(failure));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDefaultIdentity<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication().AddCookie("SyntheticProvider", "Synthetic provider", o => {
            // Return protected challenge properties to the test without an external redirect.
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 200;
                return c.Response.WriteAsJsonAsync(c.Properties.Items); };
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly);
        builder.Services.AddRazorPages();
        app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.MapRazorPages();
        app.MapGet("/fixture/cookie", async (HttpContext ctx, string key, string? email, string? xsrf, bool? verified) => {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, key) };
            if (email != null) claims.Add(new(ClaimTypes.Email, email));
            if (verified == true) claims.Add(new("email_verified", "true"));
            var props = new AuthenticationProperties(); props.Items["LoginProvider"] = "SyntheticProvider";
            if (xsrf != null) props.Items["XsrfId"] = xsrf;
            await ctx.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, "SyntheticProvider")), props);
            return Results.NoContent();
        });
        app.MapGet("/fixture/session", async (string id, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            await signIn.SignInAsync((await users.FindByIdAsync(id))!, false); return Results.NoContent();
        });
        app.MapGet("/fixture/who", (HttpContext c) => Results.Text(c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous"));
        await With(async (db, users) => {
            await db.Database.EnsureCreatedAsync();
            var roles = app.Services.CreateScope(); using (roles) {
                Assert.True((await roles.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new("Admin"))).Succeeded);
            }
            foreach (var id in new[] { "alice", "admin", "linked", "external" }) {
                var user = new ApplicationUser { Id=id, UserName=id+"@example.invalid", Email=id+"@example.invalid", EmailConfirmed=false };
                Assert.True((id == "external" ? await users.CreateAsync(user) : await users.CreateAsync(user, password)).Succeeded);
                if (id == "admin") Assert.True((await users.AddToRoleAsync(user,"Admin")).Succeeded);
                if (id is "linked" or "external") Assert.True((await users.AddLoginAsync(user,new("SyntheticProvider",id+"-key","Synthetic provider"))).Succeeded);
            }
        });
        await app.StartAsync();
    }
    async Task With(Func<ApplicationDbContext,UserManager<ApplicationUser>,Task> action) {
        await using var s=app.Services.CreateAsyncScope(); await action(s.ServiceProvider.GetRequiredService<ApplicationDbContext>(),s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }
    HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(app.Urls.Single()) };
    static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    async Task<string> Token(HttpClient c, bool manage=false) => Token(await c.GetStringAsync(manage ? Manage : "/Identity/Account/Login"));
    async Task Cookie(HttpClient c,string key="new-key",string? email="new@example.invalid",string? xsrf=null,bool verified=false) {
        var url="/fixture/cookie?key="+Uri.EscapeDataString(key)+"&verified="+verified;
        if(email!=null)url+="&email="+Uri.EscapeDataString(email);
        if(xsrf!=null)url+="&xsrf="+Uri.EscapeDataString(xsrf);
        Assert.Equal(HttpStatusCode.NoContent,(await c.GetAsync(url)).StatusCode);
    }
    static Task<HttpResponseMessage> Post(HttpClient c,string path,string? token,params (string,string)[] data) {
        var fields=data.ToDictionary(x=>x.Item1,x=>x.Item2); if(token!=null) fields["__RequestVerificationToken"]=token;
        return c.PostAsync(path,new FormUrlEncodedContent(fields));
    }
    async Task<string> Snapshot(string id) {
        string value=""; await With(async(db,users)=> { var u=(await users.FindByIdAsync(id))!;
            value=JsonSerializer.Serialize(new {u.PasswordHash,u.EmailConfirmed,u.SecurityStamp,u.ConcurrencyStamp,u.LockoutEnd,u.AccessFailedCount,
                Roles=await users.GetRolesAsync(u),Logins=(await users.GetLoginsAsync(u)).Select(x=>new {x.LoginProvider,x.ProviderKey}).ToArray()}); }); return value;
    }
    static void Capture(string name,string html) {
        var path=Environment.GetEnvironmentVariable("DOGGYDROP_EXTERNAL_CAPTURE"); if(string.IsNullOrEmpty(path))return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path,name+".html"),html);
    }
    [Theory]
    [InlineData("admin",false,false)] [InlineData("admin",true,false)] [InlineData("admin",false,true)]
    [InlineData("alice",false,false)] [InlineData("alice",true,false)] [InlineData("alice",true,true)]
    [InlineData("linked",false,false)]
    public async Task EmailCollisionNeverMutatesOrSignsIntoExistingAccount(string victim,bool post,bool verified) {
        using var c=Client(); var token=await Token(c); var before=await Snapshot(victim);
        await Cookie(c,email:victim+"@example.invalid",verified:verified);
        var r=post?await Post(c,External+"?handler=Confirmation",token,("Input.Email",victim+"@example.invalid")):await c.GetAsync(External+"?handler=Callback");
        Assert.Equal(HttpStatusCode.OK,r.StatusCode); var html=await r.Content.ReadAsStringAsync();
        Assert.Contains("Prijavi se v obstoje",html); Capture("conflict",html);
        Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who")); Assert.True(before==await Snapshot(victim));
    }
    [Theory] [InlineData("attacker@example.invalid")] [InlineData(null)]
    public async Task SubmittedVictimEmailCannotSelectExistingAdmin(string? providerEmail) {
        using var c=Client(); var token=await Token(c); var before=await Snapshot("admin");
        await Cookie(c,email:providerEmail);
        await Post(c,External+"?handler=Confirmation",token,("Input.Email","admin@example.invalid"),("UserId","admin"));
        Assert.NotEqual("admin",await c.GetStringAsync("/fixture/who")); Assert.True(before==await Snapshot("admin"));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task LinkedTupleWinsOverEmailAndSubmittedVictim(bool post) {
        using var c=Client();var token=await Token(c);await Cookie(c,"linked-key","admin@example.invalid");
        var r=post?await Post(c,External+"?handler=Confirmation",token,("Input.Email","admin@example.invalid")):await c.GetAsync(External+"?handler=Callback");
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);Assert.Equal("linked",await c.GetStringAsync("/fixture/who"));
    }
    [Theory] [InlineData("locked")] [InlineData("twofactor")] [InlineData("unconfirmed")]
    public async Task LinkedIdentityPolicyFailureNeverFallsThrough(string policy) {
        await With(async(db,users)=> { var u=(await users.FindByIdAsync("linked"))!;
            if(policy=="locked") await users.SetLockoutEndDateAsync(u,DateTimeOffset.UtcNow.AddHours(1));
            if(policy=="twofactor") { u.EmailConfirmed=true;await users.UpdateAsync(u);await users.SetTwoFactorEnabledAsync(u,true); }
            if(policy=="unconfirmed") app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail=true;
        });
        using var c=Client();await Cookie(c,"linked-key","admin@example.invalid");
        var r=await c.GetAsync(External+"?handler=Callback");Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
        Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who"));
        if(policy=="twofactor")Assert.Contains("LoginWith2fa",r.Headers.Location!.OriginalString);
        if(policy=="locked")Assert.Contains("Lockout",r.Headers.Location!.OriginalString);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task NewExternalUserHasNoPasswordRolesOrInventedEmailVerification(bool missingEmail) {
        using var c=Client(); var token=await Token(c); await Cookie(c,email:missingEmail?null:"new@example.invalid");
        var r=missingEmail?await Post(c,External+"?handler=Confirmation",token,("Input.Email","new@example.invalid")):await c.GetAsync(External+"?handler=Callback");
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode); Assert.NotEqual("anonymous",await c.GetStringAsync("/fixture/who"));
        await With(async(db,users)=> { var u=(await users.FindByEmailAsync("new@example.invalid"))!;
            Assert.False(u.EmailConfirmed);Assert.False(await users.HasPasswordAsync(u));Assert.Empty(await users.GetRolesAsync(u));Assert.Single(await users.GetLoginsAsync(u)); });
    }
    [Theory] [InlineData(null)] [InlineData("alice")] [InlineData("admin")]
    public async Task ExplicitLinkRequiresCurrentAccountXsrfBinding(string? xsrf) {
        using var c=Client();await c.GetAsync("/fixture/session?id=alice");await Cookie(c,email:"admin@example.invalid",xsrf:xsrf);
        await c.GetAsync(Manage+"?handler=LinkLoginCallback&UserId=admin");
        await With(async(db,users)=> { var owner=await users.FindByLoginAsync("SyntheticProvider","new-key");
            if(xsrf=="alice")Assert.Equal("alice",owner!.Id);else Assert.Null(owner);
            Assert.Empty(await users.GetLoginsAsync((await users.FindByIdAsync("admin"))!)); });
        Assert.Equal("alice",await c.GetStringAsync("/fixture/who"));Capture("manage",await c.GetStringAsync(Manage));
    }
    [Fact] public async Task LinkChallengeIsBoundToPrincipalNotPostedUserId() {
        using var c=Client();await c.GetAsync("/fixture/session?id=alice");var token=await Token(c,true);
        var r=await Post(c,Manage+"?handler=LinkLogin",token,("provider","SyntheticProvider"),("UserId","admin"));
        var props=JsonSerializer.Deserialize<Dictionary<string,string>>(await r.Content.ReadAsStringAsync())!;
        Assert.Equal("alice",props["XsrfId"]);Assert.Equal("SyntheticProvider",props["LoginProvider"]);
        Assert.Contains("LinkLoginCallback",props[".redirect"]);
    }
    [Fact] public async Task ExistingProviderCannotBeLinkedToAnotherAccount() {
        using var c=Client();await c.GetAsync("/fixture/session?id=alice");await Cookie(c,"linked-key",xsrf:"alice");
        await c.GetAsync(Manage+"?handler=LinkLoginCallback");
        await With(async(db,users)=>Assert.Equal("linked",(await users.FindByLoginAsync("SyntheticProvider","linked-key"))!.Id));
    }
    [Fact] public async Task AccountBoundCookieCannotBeConsumedByPublicLogin() {
        using var c=Client();await Cookie(c,xsrf:"alice");await c.GetAsync(External+"?handler=Callback");
        Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who"));
        await With(async(db,users)=>Assert.Null(await users.FindByLoginAsync("SyntheticProvider","new-key")));
    }
    [Theory] [InlineData("external",false)] [InlineData("linked",true)]
    public async Task RemovalRequiresAnotherAuthenticationMethod(string id,bool removable) {
        using var c=Client();await c.GetAsync("/fixture/session?id="+id);var token=await Token(c,true);
        await Post(c,Manage+"?handler=RemoveLogin",token,("loginProvider","SyntheticProvider"),("providerKey",id+"-key"));
        await With(async(db,users)=>Assert.Equal(removable?0:1,(await users.GetLoginsAsync((await users.FindByIdAsync(id))!)).Count));
    }
    [Fact] public async Task RemoveCannotSelectAnotherUser() {
        using var c=Client();await c.GetAsync("/fixture/session?id=alice");var token=await Token(c,true);var before=await Snapshot("linked");
        await Post(c,Manage+"?handler=RemoveLogin",token,("loginProvider","SyntheticProvider"),("providerKey","linked-key"),("UserId","linked"));
        Assert.True(before==await Snapshot("linked"));
    }
    [Theory] [InlineData("LinkLogin")] [InlineData("RemoveLogin")]
    public async Task ManageMutationsRequireAntiforgery(string handler) {
        using var c=Client();await c.GetAsync("/fixture/session?id=linked");
        Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,Manage+"?handler="+handler,null,("provider","SyntheticProvider"),("loginProvider","SyntheticProvider"),("providerKey","linked-key"))).StatusCode);
    }
    [Fact] public async Task ConfirmationRequiresAntiforgery() {
        using var c=Client();await Cookie(c);Assert.Equal(HttpStatusCode.BadRequest,(await Post(c,External+"?handler=Confirmation",null,("Input.Email","new@example.invalid"))).StatusCode);
    }
    [Fact] public async Task ManageRequiresAuthentication() {
        using var c=Client();Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Manage)).StatusCode);
    }
    [Theory] [InlineData("https://evil.example/")] [InlineData("//evil.example/")] [InlineData("/Home/UserProfile")]
    public async Task ReturnUrlIsLocal(string target) {
        using var c=Client();await Cookie(c,"linked-key");var r=await c.GetAsync(External+"?handler=Callback&returnUrl="+Uri.EscapeDataString(target));
        Assert.Equal(target.StartsWith("/Home")?target:"/",r.Headers.Location!.OriginalString);
    }
    [Fact] public async Task RemoteErrorsAreNotReflected() {
        using var c=Client();var marker="synthetic-private-error-"+Guid.NewGuid();
        var r=await c.GetAsync(External+"?handler=Callback&remoteError="+marker);
        var html=await c.GetStringAsync(r.Headers.Location);Assert.DoesNotContain(marker,html);
    }
    [Fact] public async Task ConcurrentSameEmailRegistrationNeverLinksLosingProvider() {
        using var a=Client();using var b=Client();await Cookie(a,"one");await Cookie(b,"two");
        var responses=await Task.WhenAll(a.GetAsync(External+"?handler=Callback"),b.GetAsync(External+"?handler=Callback"));
        Assert.All(responses,r=>Assert.True(r.IsSuccessStatusCode||r.StatusCode==HttpStatusCode.Redirect));
        await With(async(db,users)=> {Assert.Equal(1,await db.Users.CountAsync(u=>u.Email=="new@example.invalid"));
            Assert.Equal(1,await db.UserLogins.CountAsync(l=>l.ProviderKey=="one"||l.ProviderKey=="two"));});
    }

    [Fact] public async Task FailedLoginInsertRollsBackNewAccount() {
        failure.Enabled=true;
        using var c=Client();await Cookie(c);var r=await c.GetAsync(External+"?handler=Callback");
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who"));
        await With(async(db,users)=>Assert.Null(await users.FindByEmailAsync("new@example.invalid")));
    }
    [Fact] public async Task ConcurrentProviderRegistrationRollsBackLoser() {
        using var a=Client();using var b=Client();await Cookie(a,email:"one@example.invalid");await Cookie(b,email:"two@example.invalid");
        var responses=await Task.WhenAll(a.GetAsync(External+"?handler=Callback"),b.GetAsync(External+"?handler=Callback"));
        Assert.All(responses,r=>Assert.True(r.IsSuccessStatusCode||r.StatusCode==HttpStatusCode.Redirect));
        await With(async(db,users)=> {Assert.Equal(1,await db.UserLogins.CountAsync(l=>l.ProviderKey=="new-key"));
            Assert.Equal(1,await db.Users.CountAsync(u=>u.Email=="one@example.invalid"||u.Email=="two@example.invalid"));});
    }
    [Fact] public async Task ConcurrentRemovalRetainsLastLogin() {
        await With(async(db,users)=>Assert.True((await users.AddLoginAsync((await users.FindByIdAsync("external"))!,new("OtherProvider","second-key","Other"))).Succeeded));
        using var a=Client();using var b=Client();await a.GetAsync("/fixture/session?id=external");await b.GetAsync("/fixture/session?id=external");
        var at=await Token(a,true);var bt=await Token(b,true);
        var responses=await Task.WhenAll(
            Post(a,Manage+"?handler=RemoveLogin",at,("loginProvider","SyntheticProvider"),("providerKey","external-key")),
            Post(b,Manage+"?handler=RemoveLogin",bt,("loginProvider","OtherProvider"),("providerKey","second-key")));
        Assert.All(responses,r=>Assert.Equal(HttpStatusCode.Redirect,r.StatusCode));
        await With(async(db,users)=>Assert.Single(await users.GetLoginsAsync((await users.FindByIdAsync("external"))!)));
    }
    [Fact] public async Task MissingProviderCookieCannotCreateSession() {
        using var c=Client();var token=await Token(c);
        await Post(c,External+"?handler=Confirmation",token,("Input.Email","admin@example.invalid"));
        Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who"));
    }
    [Fact] public async Task UnknownProviderCannotBeChallenged() {
        using var c=Client();var token=await Token(c);
        Assert.Equal(HttpStatusCode.Redirect,(await Post(c,External,token,("provider","Unregistered"))).StatusCode);
    }
    [Fact] public async Task MissingProviderEmailShowsEncodedAccessibleRegistrationForm() {
        using var c=Client();await Cookie(c,email:null);var html=await c.GetStringAsync(External+"?handler=Callback");
        Assert.Contains("Input_Email",html);Capture("registration",html);
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("not-an-email")]
    public async Task MissingOrInvalidRegistrationEmailIsValidationNotServerError(string? email) {
        using var c=Client();var token=await Token(c);await Cookie(c,email:null);
        var data=email==null?Array.Empty<(string,string)>():new[]{("Input.Email",email)};
        var r=await Post(c,External+"?handler=Confirmation",token,data);
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("validation-summary-errors",await r.Content.ReadAsStringAsync());
        Assert.Equal("anonymous",await c.GetStringAsync("/fixture/who"));
        await With(async(db,users)=>Assert.Equal(4,await db.Users.CountAsync()));
    }
    sealed class FailLoginInsert : DbCommandInterceptor {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default) {
            if(Enabled && command.CommandText.Contains("INSERT INTO \"AspNetUserLogins\"")) throw new InvalidOperationException("Synthetic login insert failure");
            return base.ReaderExecutingAsync(command,eventData,result,cancellationToken);
        }
    }
    public async Task DisposeAsync() {await app.StopAsync();await app.DisposeAsync();File.Delete(database);}
}
