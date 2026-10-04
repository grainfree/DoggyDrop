using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Text.RegularExpressions;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
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
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

// Boolean assertions deliberately avoid printing password-bearing values on failure.
#pragma warning disable xUnit2008, xUnit2009, xUnit2012

// Actual Identity cookie, Razor, antiforgery, limiter and EF store. No application startup,
// bootstrap, migrations, external login, email or production providers run in this host.
public sealed partial class IdentityAdversarialTests : IAsyncLifetime
{
    private const string Change = "/Identity/Account/Manage/ChangePassword";
    private const string Set = "/Identity/Account/Manage/SetPassword";
    private const string Login = "/Identity/Account/Login";
    private readonly string initial = Synthetic();
    private readonly string replacement = Synthetic();
    private readonly string database = Path.Combine(Path.GetTempPath(), $"identity-audit-{Guid.NewGuid():N}.db");
    private readonly TestClock clock = new();
    private readonly CapturedLogs logs = new();
    private readonly TestMail mail = new();
    private WebApplication app = null!;
    private string root = "";
    private static string Synthetic() => "Synthetic-" + Guid.NewGuid().ToString("N") + "!aA7";

    public async Task InitializeAsync()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "DoggyDrop.sln"))) directory = directory.Parent;
        root = directory!.FullName;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions {
            ApplicationName = typeof(HomeController).Assembly.GetName().Name, EnvironmentName = "Production",
            ContentRootPath = Path.Combine(root, "DoggyDrop"), Args = [] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddDefaultIdentity<ApplicationUser>(o => o.SignIn.RequireConfirmedAccount = false)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.ConfigureApplicationCookie(o => {
            o.TimeProvider = clock;
            o.Events.OnRedirectToLogin = WalkPointCookieRedirects.ToLogin;
            o.Events.OnRedirectToAccessDenied = WalkPointCookieRedirects.ToAccessDenied;
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(o => o.TimeProvider = clock);
        builder.Services.AddPasswordSettings();
        builder.Services.AddIdentityRequestSafety();
        builder.Services.AddSingleton<IEmailSender>(mail);
        builder.Services.AddScoped<ILocalLeaderboardService, LocalLeaderboardService>();
        builder.Services.AddScoped<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IGamificationCalendar, GamificationCalendar>();
        builder.Services.AddScoped<IWeeklyGoalsService, WeeklyGoalsService>();
        builder.Services.AddScoped<IGamificationService, GamificationService>();
        builder.Services.AddScoped<IDogProgressionService, DogProgressionService>();
        builder.Services.AddScoped<IUserAchievementService, UserAchievementService>();
        builder.Services.AddSingleton<ISeasonalEventService, SeasonalEventService>();
        builder.Services.AddSingleton<IMapStampService, MapStampService>();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(HomeController).Assembly).AddControllersAsServices();
        builder.Services.AddTransient(sp => new HomeController(sp.GetRequiredService<ILogger<HomeController>>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(), null!, mail, sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IGamificationService>(), sp.GetRequiredService<ISeasonalEventService>(),
            sp.GetRequiredService<ILocalLeaderboardService>(), sp.GetRequiredService<IMapStampService>(), sp.GetRequiredService<IUserAchievementService>()));
        builder.Services.AddScoped<AccountDataDeletion>();
        builder.Services.AddScoped<PersonalDataExport>();
        builder.Services.AddSingleton<IUserMediaCleanup, PrivacyNoMedia>();
        builder.Services.AddRazorPages();
        app = builder.Build(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}"); app.MapRazorPages();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await db.Database.EnsureCreatedAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            foreach (var id in new[] { "alice", "bob", "admin", "external" })
            {
                var user = new ApplicationUser { Id = id, UserName = id + "@example.invalid", Email = id + "@example.invalid", DisplayName = "Testni uporabnik", EmailConfirmed = true };
                Assert.True((id == "external" ? await users.CreateAsync(user) : await users.CreateAsync(user, initial)).Succeeded);
                if (id == "admin") Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
                if (id == "external") Assert.True((await users.AddLoginAsync(user, new UserLoginInfo("SyntheticProvider", "fixture-only", "Synthetic provider"))).Succeeded);
            }
        }
        // Only in this isolated host: simulate the result of an external provider's authenticated login.
        app.MapGet("/fixture/session", async (string id, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            await signIn.SignInAsync((await users.FindByIdAsync(id))!,false); return Results.NoContent();
        });
        app.MapGet("/fixture/external", async (UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) => {
            await signIn.SignInAsync((await users.FindByIdAsync("external"))!, false); return Results.NoContent();
        });
        await app.StartAsync();
    }

    private HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
    private async Task<HttpClient> SignedIn(string id = "alice")
    {
        var client = Client();
        if (id == "external") Assert.Equal(HttpStatusCode.NoContent, (await client.GetAsync("/fixture/external")).StatusCode);
        else Assert.Equal(HttpStatusCode.Redirect, (await LoginWith(client, id, initial)).StatusCode);
        return client;
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<HttpResponseMessage> LoginWith(HttpClient client, string id, string password)
    {
        var html = await client.GetStringAsync(Login);
        return await client.PostAsync(Login, new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"] = Token(html), ["Input.Email"] = id + "@example.invalid", ["Input.Password"] = password
        }));
    }
    private async Task<(bool PasswordValid, string? Stamp, int Failures, string? Hash)> State(string id, string password)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(id))!;
        return (await users.CheckPasswordAsync(user,password),user.SecurityStamp,user.AccessFailedCount,user.PasswordHash);
    }
    private void NoEcho(string response, params string[] secrets)
    {
        foreach (var secret in secrets.Concat(new[] { initial, replacement }))
        {
            Assert.False(response.Contains(secret, StringComparison.Ordinal));
            Assert.False(WebUtility.HtmlDecode(response).Contains(secret, StringComparison.Ordinal));
            Assert.False(logs.Text.Contains(secret, StringComparison.Ordinal));
        }
        foreach (Match input in Regex.Matches(response, "<input[^>]*type=\"password\"[^>]*>"))
            Assert.False(Regex.IsMatch(input.Value, "value=\"[^\"]+\""));
    }
    private static void Capture(string name, string html)
    {
        var path = Environment.GetEnvironmentVariable("DOGGYDROP_IDENTITY_CAPTURE");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name + ".html"), html);
    }

    async Task With(Func<ApplicationDbContext,UserManager<ApplicationUser>,Task> action) {
        await using var s=app.Services.CreateAsyncScope(); await action(s.ServiceProvider.GetRequiredService<ApplicationDbContext>(),s.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
    }
    async Task<HttpResponseMessage> Form(HttpClient c,string path,Dictionary<string,string> data,bool token=true,string? host=null) {
        if(token)data["__RequestVerificationToken"]=Token(await c.GetStringAsync(path.StartsWith("/Identity/Account/ResetPassword") ? Login : path.Split('?')[0]));
        using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=new FormUrlEncodedContent(data)};
        if(host!=null)request.Headers.Host=host;
        return await c.SendAsync(request);
    }
    [Fact] public async Task ResetEmailCannotUseAttackerHost() {
        using var c=Client();var r=await Form(c,"/Identity/Account/ForgotPassword",new(){["Input.Email"]="admin@example.invalid"},host:"attacker.example");
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
        var message=mail.Messages.Single(); Assert.False(message.Html.Contains("attacker.example"));
        Assert.True(message.Html.Contains("https://doggydrop.app/Identity/Account/ResetPassword"));
    }
    [Fact] public async Task ProfileMutationRequiresAntiforgery() {
        using var c=await SignedIn();var before="";
        await With(async(db,users)=>before=(await users.FindByIdAsync("alice"))!.DisplayName!);
        var r=await Form(c,"/Home/UpdateProfile",new(){["DisplayName"]="injected"},token:false);
        Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        await With(async(db,users)=>Assert.Equal(before,(await users.FindByIdAsync("alice"))!.DisplayName));
    }
    [Fact] public async Task RepeatedPasswordFailuresEnforceIdentityLockout() {
        using var c=Client();for(var i=0;i<6;i++)await LoginWith(c,"admin",replacement);
        await With(async(db,users)=>Assert.True(await users.IsLockedOutAsync((await users.FindByIdAsync("admin"))!)));
        await LoginWith(c,"admin",initial);
        Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Change)).StatusCode);
    }
    [Fact] public async Task RecoveryEmailRequestsAreBoundedBeforeSending() {
        using var c=Client();int rejected=0;
        for(var i=0;i<22;i++) {
            var r=await Form(c,"/Identity/Account/ForgotPassword",new(){["Input.Email"]="alice@example.invalid"});
            if(r.StatusCode==HttpStatusCode.TooManyRequests)rejected++;
        }
        Assert.True(rejected>0);Assert.True(mail.Messages.Count<=20);
    }

    [Fact] public async Task EmailChangeCollisionIsAtomic() {
        string code="";await With(async(db,users)=>code=await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync("alice"))!,"bob@example.invalid"));
        using var c=Client();await c.GetAsync("/Identity/Account/ConfirmEmailChange?userId=alice&email=bob%40example.invalid&code="+Encode(code));
        await With(async(db,users)=> {var a=(await users.FindByIdAsync("alice"))!;Assert.True(a.Email=="alice@example.invalid");Assert.True(a.UserName=="alice@example.invalid");});
    }
    static string Encode(string token)=>WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    [Theory] [InlineData("/Identity/Account/Register")] [InlineData("/Identity/Account/ResendEmailConfirmation")] [InlineData("/Identity/Account/Manage/Email?handler=SendVerificationEmail")]
    public async Task OtherIdentityEmailsCannotUseAttackerHost(string path) {
        using var c=Client();if(path.Contains("/Manage/"))await c.GetAsync("/fixture/session?id=alice");
        var data=new Dictionary<string,string>{["Input.Email"]=path.Contains("Register")?"fresh@example.invalid":"alice@example.invalid",["Input.Password"]=initial,["Input.ConfirmPassword"]=initial,["Input.AcceptTerms"]="true",["Input.NewEmail"]="new@example.invalid"};
        await Form(c,path,data,host:"attacker.example");Assert.NotEmpty(mail.Messages);
        Assert.All(mail.Messages,m=>Assert.False(m.Html.Contains("attacker.example")));
        Assert.True(mail.Messages.Any(m=>m.Html.Contains("https://doggydrop.app/Identity/Account/ConfirmEmail")));
    }
    [Fact] public async Task RegistrationIgnoresSecurityOverpostingAndNormalizesEmail() {
        using var c=Client();var r=await Form(c,"/Identity/Account/Register",new(){["Input.Email"]="New.User@Example.Invalid",["Input.Password"]=initial,["Input.ConfirmPassword"]=initial,["Input.AcceptTerms"]="true",["Input.Id"]="admin",["Input.UserId"]="admin",["Input.Role"]="Admin",["Input.IsAdmin"]="true",["Input.EmailConfirmed"]="true",["Input.PasswordHash"]="injected",["Roles"]="Admin"});
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
        await With(async(db,users)=>{var u=(await users.FindByEmailAsync("new.user@example.invalid"))!;Assert.NotEqual("admin",u.Id);Assert.False(u.EmailConfirmed);Assert.Empty(await users.GetRolesAsync(u));Assert.True(await users.CheckPasswordAsync(u,initial));});
    }
    [Fact] public async Task DuplicateRegistrationCannotMutateAdmin() {
        using var c=Client();var before=await State("admin",initial);
        var r=await Form(c,"/Identity/Account/Register",new(){["Input.Email"]="ADMIN@EXAMPLE.INVALID",["Input.Password"]=replacement,["Input.ConfirmPassword"]=replacement,["Input.AcceptTerms"]="true"});
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);NoEcho(await r.Content.ReadAsStringAsync());Assert.True(before==await State("admin",initial));
    }
    [Fact] public async Task UnknownAndWrongPasswordHaveSameGenericFeedback() {
        using var c=Client();var a=await LoginWith(c,"nobody",replacement);var b=await LoginWith(c,"alice",replacement);
        Assert.Equal(a.StatusCode,b.StatusCode);Assert.Contains("Invalid login attempt",await a.Content.ReadAsStringAsync());Assert.Contains("Invalid login attempt",await b.Content.ReadAsStringAsync());
    }
    [Theory] [InlineData("/Identity/Account/Login")] [InlineData("/Identity/Account/Register")] [InlineData("/Identity/Account/Logout")]
    public async Task PublicAccountPostsRequireAntiforgery(string path) {
        using var c=await SignedIn();Assert.Equal(HttpStatusCode.BadRequest,(await Form(c,path,new(){["Input.Email"]="alice@example.invalid",["Input.Password"]=initial},token:false)).StatusCode);
    }
    [Theory] [InlineData("cross-user")] [InlineData("tampered")] [InlineData("expired")] [InlineData("replay")] [InlineData("after-change")]
    public async Task ResetTokensCannotBeSubstitutedOrReused(string attack) {
        string code="";await With(async(db,users)=>code=await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync("alice"))!));
        if(attack=="tampered")code+="tampered";
        if(attack=="expired")app.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan=TimeSpan.FromSeconds(-1);
        if(attack=="after-change")await With(async(db,users)=>Assert.True((await users.ChangePasswordAsync((await users.FindByIdAsync("alice"))!,initial,replacement)).Succeeded));
        using var c=Client();var target=attack=="cross-user"?"admin":"alice";
        if(attack=="replay") {
            var first=await Form(c,"/Identity/Account/ResetPassword",new(){["Input.Email"]="alice@example.invalid",["Input.Password"]=replacement,["Input.ConfirmPassword"]=replacement,["Input.Code"]=code});Assert.Equal(HttpStatusCode.Redirect,first.StatusCode);
        }
        var before=await State(target,initial);
        var r=await Form(c,"/Identity/Account/ResetPassword",new(){["Input.Email"]=target+"@example.invalid",["Input.Password"]=initial,["Input.ConfirmPassword"]=initial,["Input.Code"]=code,["UserId"]="admin"});
        Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.True(before==await State(target,initial));NoEcho(await r.Content.ReadAsStringAsync());
    }
    [Fact] public async Task ValidResetChangesStampWithoutCreatingSession() {
        string code="";await With(async(db,users)=>code=await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync("alice"))!));var before=await State("alice",initial);
        using var c=Client();var r=await Form(c,"/Identity/Account/ResetPassword",new(){["Input.Email"]="alice@example.invalid",["Input.Password"]=replacement,["Input.ConfirmPassword"]=replacement,["Input.Code"]=code});
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);var after=await State("alice",replacement);Assert.True(after.PasswordValid);Assert.True(before.Stamp!=after.Stamp);Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Change)).StatusCode);
    }
    [Theory] [InlineData("cross-user")] [InlineData("tampered")] [InlineData("expired")]
    public async Task EmailConfirmationTokenIsBoundToAccount(string attack) {
        string code="";await With(async(db,users)=>{foreach(var id in new[]{"alice","admin"}){var u=(await users.FindByIdAsync(id))!;u.EmailConfirmed=false;await users.UpdateAsync(u);}code=await users.GenerateEmailConfirmationTokenAsync((await users.FindByIdAsync("alice"))!);});
        if(attack=="tampered")code+="tampered";
        if(attack=="expired")app.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan=TimeSpan.FromSeconds(-1);
        var target=attack=="cross-user"?"admin":"alice";
        using var c=Client();await c.GetAsync("/Identity/Account/ConfirmEmail?userId="+target+"&code="+Encode(code));
        await With(async(db,users)=>Assert.False((await users.FindByIdAsync(target))!.EmailConfirmed));
        Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Change)).StatusCode);
    }
    [Fact] public async Task ProfileOverpostingCannotSelectAdminAndHostileNameIsEncoded() {
        using var c=await SignedIn();var hostile="<script>alert('profile')</script>";var token=Token(await c.GetStringAsync(Change));
        var r=await Form(c,"/Home/UpdateProfile",new(){["__RequestVerificationToken"]=token,["DisplayName"]=hostile,["UserId"]="admin",["Role"]="Admin",["EmailConfirmed"]="true",["PasswordHash"]="injected",["Email"]="admin@example.invalid"},token:false);
        Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
        await With(async(db,users)=> {var a=(await users.FindByIdAsync("alice"))!;Assert.Equal(hostile,a.DisplayName);Assert.Empty(await users.GetRolesAsync(a));Assert.True(a.Email=="alice@example.invalid");Assert.NotEqual(hostile,(await users.FindByIdAsync("admin"))!.DisplayName);});
        var html=await c.GetStringAsync("/Home/UserProfile");Assert.False(html.Contains(hostile));Assert.Contains("&lt;script&gt;",html);
    }
    [Theory] [InlineData("Index")] [InlineData("Email")] [InlineData("ExternalLogins")] [InlineData("ChangePassword")] [InlineData("SetPassword")]
    public async Task AccountManagePagesRequirePrincipal(string page) {
        using var c=Client();var r=await c.GetAsync("/Identity/Account/Manage/"+page+"?UserId=admin");Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
    }
    [Theory] [InlineData("/AdminBins")] [InlineData("/AdminPlaces")] [InlineData("/AdminWaterPoints")]
    public async Task OrdinarySessionCannotReadAdminPages(string path) {
        using var c=await SignedIn();var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);Assert.Contains("AccessDenied",r.Headers.Location!.OriginalString);
    }
    [Fact] public void AllAdminControllerActionsEnforceAdminRole() {
        var types=typeof(HomeController).Assembly.GetTypes().Where(t=>t.Name.StartsWith("Admin")&&t.Name.EndsWith("Controller")).ToArray();Assert.NotEmpty(types);
        foreach(var t in types){Assert.Contains(t.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>(),a=>a.Roles=="Admin");
            Assert.DoesNotContain(t.GetMethods(),m=>m.GetCustomAttributes(true).OfType<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Any());}
    }

    [Theory] [InlineData("user")] [InlineData("email")] [InlineData("token")]
    public async Task EmailChangeTokenCannotSelectAnotherUserOrAddress(string attack) {
        string code="";await With(async(db,users)=>code=await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync("alice"))!,"target@example.invalid"));
        using var c=Client();var id=attack=="user"?"admin":"alice";var email=attack=="email"?"different@example.invalid":"target@example.invalid";
        if(attack=="token")code+="tamper";
        var before=await State(id,initial);await c.GetAsync("/Identity/Account/ConfirmEmailChange?userId="+id+"&email="+email+"&code="+Encode(code));
        await With(async(db,users)=>Assert.True((await users.FindByIdAsync(id))!.Email==id+"@example.invalid"));Assert.True(before==await State(id,initial));
    }
    [Fact] public async Task ValidEmailChangeIsAtomicReplaySafeAndDoesNotSignInAnonymous() {
        string code="";await With(async(db,users)=>code=await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync("alice"))!,"target@example.invalid"));
        using var c=Client();var path="/Identity/Account/ConfirmEmailChange?userId=alice&email=target@example.invalid&code="+Encode(code);
        var html=await c.GetStringAsync(path);Capture("email-change-success",html);
        var after=await State("alice",initial);
        await With(async(db,users)=>{var u=(await users.FindByIdAsync("alice"))!;Assert.True(u.Email=="target@example.invalid"&&u.UserName==u.Email&&u.EmailConfirmed);});
        var replay=await c.GetStringAsync(path);Capture("email-change-error",replay);Assert.True(after==await State("alice",initial));
        Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Change)).StatusCode);
    }
    [Fact] public async Task ConcurrentEmailChangesCannotCreateDuplicateEmails() {
        string a="",b="";await With(async(db,users)=>{a=await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync("alice"))!,"target@example.invalid");b=await users.GenerateChangeEmailTokenAsync((await users.FindByIdAsync("bob"))!,"target@example.invalid");});
        using var ca=Client();using var cb=Client();
        await Task.WhenAll(ca.GetAsync("/Identity/Account/ConfirmEmailChange?userId=alice&email=target@example.invalid&code="+Encode(a)),cb.GetAsync("/Identity/Account/ConfirmEmailChange?userId=bob&email=target@example.invalid&code="+Encode(b)));
        await With(async(db,users)=>{Assert.Equal(1,await db.Users.CountAsync(u=>u.Email=="target@example.invalid"));Assert.True(await db.Users.AllAsync(u=>u.Email==u.UserName));});
    }
    [Fact] public async Task ConcurrentResetsHaveOnlyOneWinner() {
        string token="";await With(async(db,users)=>token=await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync("alice"))!));
        using var ca=Client();using var cb=Client();var ta=Token(await ca.GetStringAsync(Login));var tb=Token(await cb.GetStringAsync(Login));
        var results=await Task.WhenAll(Form(ca,"/Identity/Account/ResetPassword",new(){["__RequestVerificationToken"]=ta,["Input.Email"]="alice@example.invalid",["Input.Password"]=replacement,["Input.ConfirmPassword"]=replacement,["Input.Code"]=token},token:false),Form(cb,"/Identity/Account/ResetPassword",new(){["__RequestVerificationToken"]=tb,["Input.Email"]="alice@example.invalid",["Input.Password"]=initial,["Input.ConfirmPassword"]=initial,["Input.Code"]=token},token:false));
        Assert.Equal(1,results.Count(r=>r.StatusCode==HttpStatusCode.Redirect));
    }
    [Fact] public async Task ConfirmationReplayIsIdempotentAndNeverSignsIn() {
        string code="";await With(async(db,users)=>{var u=(await users.FindByIdAsync("alice"))!;u.EmailConfirmed=false;await users.UpdateAsync(u);code=await users.GenerateEmailConfirmationTokenAsync(u);});
        using var c=Client();var path="/Identity/Account/ConfirmEmail?userId=alice&code="+Encode(code);await c.GetAsync(path);await c.GetAsync(path);
        await With(async(db,users)=>Assert.True((await users.FindByIdAsync("alice"))!.EmailConfirmed));Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync(Change)).StatusCode);
    }
    [Theory] [InlineData("https://attacker.example")] [InlineData("//attacker.example")] [InlineData("/\\attacker.example")] [InlineData("%2f%2fattacker.example")]
    public async Task LoginAndLogoutNeverRedirectToExternalInput(string target) {
        using var c=Client();var path=Login+"?returnUrl="+Uri.EscapeDataString(target);
        var r=await Form(c,path,new(){["Input.Email"]="alice@example.invalid",["Input.Password"]=initial});
        Assert.True(r.Headers.Location==null||!r.Headers.Location.IsAbsoluteUri);
        using var signed=await SignedIn();
        var logout=await Form(signed,"/Identity/Account/Logout?returnUrl="+Uri.EscapeDataString(target),new(){["__RequestVerificationToken"]=Token(await signed.GetStringAsync(Change))},token:false);
        Assert.True(logout.Headers.Location==null||!logout.Headers.Location.IsAbsoluteUri);
    }
    [Fact] public async Task LogoutGetDoesNotMutateSession() {
        using var c=await SignedIn();await c.GetAsync("/Identity/Account/Logout");Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(Change)).StatusCode);
    }
    [Fact] public async Task ManagePhoneOverpostingIsOwnAccountOnly() {
        using var c=await SignedIn();var before=await State("admin",initial);
        await Form(c,"/Identity/Account/Manage/Index?UserId=admin",new(){["Input.PhoneNumber"]="+38640123456",["Input.Email"]="admin@example.invalid",["Input.Role"]="Admin",["Input.UserId"]="admin"});
        await With(async(db,users)=>{Assert.Equal("+38640123456",(await users.FindByIdAsync("alice"))!.PhoneNumber);Assert.Null((await users.FindByIdAsync("admin"))!.PhoneNumber);});Assert.True(before==await State("admin",initial));
    }
    [Fact] public async Task EmailBudgetIsSharedAcrossSendingRoutesButDoesNotBlockLoginOrReads() {
        using var c=Client();for(int i=0;i<20;i++)await Form(c,"/Identity/Account/ResendEmailConfirmation",new(){["Input.Email"]="alice@example.invalid"});
        var before=mail.Messages.Count;var r=await Form(c,"/Identity/Account/ForgotPassword",new(){["Input.Email"]="alice@example.invalid"});
        Assert.Equal(HttpStatusCode.TooManyRequests,r.StatusCode);Assert.Equal(before,mail.Messages.Count);
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/Identity/Account/ForgotPassword")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await LoginWith(c,"alice",initial)).StatusCode);
    }
    [Fact] public async Task ForwardedHostCannotChangeRecoveryEmailOrigin() {
        using var c=Client();c.DefaultRequestHeaders.Add("X-Forwarded-Host","attacker.example");c.DefaultRequestHeaders.Add("X-Forwarded-Proto","http");
        await Form(c,"/Identity/Account/ForgotPassword",new(){["Input.Email"]="alice@example.invalid"},host:"attacker.example");
        Assert.All(mail.Messages,m=>Assert.False(m.Html.Contains("attacker.example")));
    }
    [Fact] public void CookieAndStampDefaultsRemainFrameworkManaged() {
        var options=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        Assert.True(options.Cookie.HttpOnly);Assert.Equal(SameSiteMode.Lax,options.Cookie.SameSite);Assert.True(options.SlidingExpiration);
        Assert.Equal(TimeSpan.FromDays(14),options.ExpireTimeSpan);Assert.Equal(TimeSpan.FromMinutes(30),app.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval);
        Assert.Equal("/Identity/Account/Login",options.LoginPath.Value);Assert.Equal("/Identity/Account/AccessDenied",options.AccessDeniedPath.Value);
    }

    [Theory] [InlineData("ResetPassword")] [InlineData("ConfirmEmail")] [InlineData("ConfirmEmailChange")]
    public async Task TokenPagesDoNotForwardTokenUrlsToThirdPartyResources(string page) {
        using var c=Client();var r=await c.GetAsync("/Identity/Account/"+page+"?userId=alice&email=target@example.invalid&code="+Encode("synthetic-invalid-token"));
        var html=await r.Content.ReadAsStringAsync();Capture(page,html);
        Assert.True(r.Headers.TryGetValues("Referrer-Policy",out var values)&&values.Contains("no-referrer"));
        Assert.Contains("name=\"referrer\" content=\"no-referrer\"",html);
    }
    public async Task DisposeAsync() {
        await app.StopAsync();await app.DisposeAsync();foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(database+suffix))File.Delete(database+suffix);
    }
    sealed class TestMail : IEmailSender {
        public ConcurrentQueue<(string To,string Html)> Messages {get;}=new();
        public Task SendEmailAsync(string email,string subject,string htmlMessage){Messages.Enqueue((email,htmlMessage));return Task.CompletedTask;}
    }
    private sealed class TestClock : TimeProvider {
        private DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan by)=>now+=by;
    }
    private sealed class CapturedLogs : ILoggerProvider {
        private readonly ConcurrentQueue<string> lines=new(); public string Text=>string.Join('\n',lines);
        public ILogger CreateLogger(string categoryName)=>new Capture(lines);public void Dispose(){}
        private sealed class Capture(ConcurrentQueue<string> lines):ILogger {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel logLevel)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>lines.Enqueue(formatter(state,exception));
        }
    }
}
