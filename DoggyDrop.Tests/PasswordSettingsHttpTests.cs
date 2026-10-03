using System.Collections.Concurrent;
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
#pragma warning disable xUnit2008, xUnit2009

// Actual Identity cookie, Razor, antiforgery, limiter and EF store. No application startup,
// bootstrap, migrations, external login, email or production providers run in this host.
public sealed class PasswordSettingsHttpTests : IAsyncLifetime
{
    private const string Change = "/Identity/Account/Manage/ChangePassword";
    private const string Set = "/Identity/Account/Manage/SetPassword";
    private const string Login = "/Identity/Account/Login";
    private readonly string initial = Synthetic();
    private readonly string replacement = Synthetic();
    private readonly string database = Path.Combine(Path.GetTempPath(), $"password-settings-{Guid.NewGuid():N}.db");
    private readonly TestClock clock = new();
    private readonly CapturedLogs logs = new();
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
            sp.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!, sp.GetRequiredService<ApplicationDbContext>(),
            sp.GetRequiredService<IGamificationService>(), sp.GetRequiredService<ISeasonalEventService>(),
            sp.GetRequiredService<ILocalLeaderboardService>(), sp.GetRequiredService<IMapStampService>(), sp.GetRequiredService<IUserAchievementService>()));
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
    private async Task<HttpResponseMessage> Submit(HttpClient client, string? current = null, string? next = null,
        string? confirm = null, string path = Change, string? tokenOverride = null, Dictionary<string,string>? extra = null)
    {
        var page = await client.GetStringAsync(path);
        var data = new Dictionary<string,string> {
            ["Input.CurrentPassword"] = current ?? initial, ["Input.NewPassword"] = next ?? replacement,
            ["Input.ConfirmPassword"] = confirm ?? next ?? replacement
        };
        if (tokenOverride != "missing") data["__RequestVerificationToken"] = tokenOverride ?? Token(page);
        if (extra != null) foreach (var pair in extra) data[pair.Key] = pair.Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(data));
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
        var path = Environment.GetEnvironmentVariable("DOGGYDROP_PASSWORD_CAPTURE");
        if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, name + ".html"), html);
    }

    [Theory] [InlineData("alice")] [InlineData("admin")]
    public async Task CorrectCurrentPasswordChangesOwnPasswordAndRefreshesSession(string id)
    {
        using var client = await SignedIn(id); var before = await State(id, initial);
        var response = await Submit(client); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Change, response.Headers.Location!.OriginalString);
        var after = await State(id, replacement); Assert.True(after.PasswordValid); Assert.False((await State(id, initial)).PasswordValid);
        Assert.True(before.Stamp != after.Stamp); Assert.Equal(before.Failures, after.Failures);
        var html = await client.GetStringAsync(Change); Assert.Contains("role=\"status\"", html);
        Assert.Contains("Geslo je bilo uspešno spremenjeno.", WebUtility.HtmlDecode(html)); NoEcho(html, after.Hash!); Capture("success",html);
        using var oldLogin = Client(); Assert.Equal(HttpStatusCode.OK,(await LoginWith(oldLogin,id,initial)).StatusCode);
        using var newLogin = Client(); Assert.Equal(HttpStatusCode.Redirect,(await LoginWith(newLogin,id,replacement)).StatusCode);
        if (id == "admin")
        {
            await using var scope = app.Services.CreateAsyncScope(); var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsInRoleAsync((await users.FindByIdAsync(id))!,"Admin"));
        }
    }
    [Fact]
    public async Task WrongCurrentPasswordPreservesPasswordAndDoesNotIncrementLoginLockout()
    {
        using var client = await SignedIn(); var before = await State("alice",initial);
        var wrong = Synthetic(); var response = await Submit(client,current:wrong);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); var html=await response.Content.ReadAsStringAsync();
        Assert.Contains("Trenutno geslo ni pravilno.",html); NoEcho(html,wrong); Capture("errors",html);
        var after=await State("alice",initial); Assert.True(after.PasswordValid); Assert.True(before.Stamp==after.Stamp);
        Assert.Equal(before.Failures,after.Failures); Assert.False((await State("alice",replacement)).PasswordValid);
    }
    [Theory]
    [InlineData("Aa1!")] [InlineData("lowercase1!")] [InlineData("UPPERCASE1!")]
    [InlineData("NoNumbersHere!")] [InlineData("NoSpecialCharacter7")] [InlineData("qzv")]
    public async Task IdentityPolicyRemainsAuthoritative(string weak)
    {
        using var client = await SignedIn(); var before=await State("alice",initial);
        var response=await Submit(client,next:weak); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync(); Assert.Contains("field-validation-error",html); NoEcho(html,weak);
        if (weak == "qzv") Capture("policy-errors",html);
        var after=await State("alice",initial); Assert.True(after.PasswordValid); Assert.True(before.Stamp==after.Stamp);
    }
    [Fact]
    public async Task MismatchIsRejectedWithoutMutation()
    {
        using var client=await SignedIn(); var mismatch=Synthetic();
        var response=await Submit(client,confirm:mismatch); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync(); Assert.Contains("Novi gesli se ne ujemata.",html); NoEcho(html,mismatch);
        Assert.True((await State("alice",initial)).PasswordValid);
    }
    [Theory] [InlineData("Input.CurrentPassword")] [InlineData("Input.NewPassword")] [InlineData("Input.ConfirmPassword")]
    public async Task AllFieldsRequired(string field)
    {
        using var client=await SignedIn(); var response=await Submit(client,extra:new() { [field]="" });
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); NoEcho(await response.Content.ReadAsStringAsync());
        Assert.True((await State("alice",initial)).PasswordValid);
    }
    [Fact]
    public async Task SamePasswordFollowsIdentityBehaviorAndStillUpdatesStamp()
    {
        using var client=await SignedIn(); var before=await State("alice",initial);
        Assert.Equal(HttpStatusCode.Redirect,(await Submit(client,next:initial)).StatusCode);
        var after=await State("alice",initial); Assert.True(after.PasswordValid); Assert.True(before.Stamp!=after.Stamp);
    }
    [Theory] [InlineData(Change,false)] [InlineData(Change,true)] [InlineData(Set,false)] [InlineData(Set,true)]
    public async Task AnonymousIsChallenged(string path,bool post)
    {
        using var client=Client(); var response=post ? await client.PostAsync(path,new FormUrlEncodedContent([])) : await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode); Assert.Contains(Login,response.Headers.Location!.OriginalString);
        Assert.True((await State("alice",initial)).PasswordValid);
    }
    [Theory] [InlineData(Change,"missing")] [InlineData(Change,"invalid")] [InlineData(Set,"missing")] [InlineData(Set,"invalid")]
    public async Task AntiforgeryIsRequired(string path,string token)
    {
        using var client=await SignedIn(path==Set ? "external" : "alice");
        Assert.Equal(HttpStatusCode.BadRequest,(await Submit(client,path:path,tokenOverride:token)).StatusCode);
        Assert.True((await State("alice",initial)).PasswordValid); Assert.Null((await State("external",replacement)).Hash);
    }
    [Theory] [InlineData("alice")] [InlineData("admin")]
    public async Task TargetAccountAndReturnUrlTamperingCannotChangeOtherUser(string id)
    {
        using var client=await SignedIn(id);
        var path=Change+"?UserId=bob&email=bob%40example.invalid&returnUrl=https%3A%2F%2Fevil.invalid";
        var response=await Submit(client,path:path,extra:new() { ["UserId"]="bob",["Input.UserId"]="bob",["Email"]="bob@example.invalid" });
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode); Assert.StartsWith(Change,response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("evil.invalid",response.Headers.Location.OriginalString);
        Assert.True((await State(id,replacement)).PasswordValid); Assert.True((await State("bob",initial)).PasswordValid);
        Assert.False((await State("bob",replacement)).PasswordValid);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsync(Change+"/bob",new FormUrlEncodedContent([]))).StatusCode);
    }
    [Fact]
    public async Task ExternalOnlyAccountUsesExistingSetPasswordFlow()
    {
        using var client=await SignedIn("external"); var redirect=await client.GetAsync(Change);
        Assert.Equal(Set,redirect.Headers.Location!.OriginalString);
        var html=await client.GetStringAsync(Set); Assert.DoesNotContain("current-password",html); NoEcho(html); Capture("set",html);
        var response=await Submit(client,path:Set); Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.True((await State("external",replacement)).PasswordValid);
        var success=await client.GetStringAsync(Change); Assert.Contains("nastavljeno",success); NoEcho(success);
        await using var scope=app.Services.CreateAsyncScope(); var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await users.GetLoginsAsync((await users.FindByIdAsync("external"))!));
    }
    [Fact]
    public async Task SetPasswordCannotBypassExistingCurrentPasswordRequirement()
    {
        using var client=await SignedIn(); Assert.Equal(Change,(await client.GetAsync(Set)).Headers.Location!.OriginalString);
        var token=Token(await client.GetStringAsync(Change));
        var response=await client.PostAsync(Set,new FormUrlEncodedContent(new Dictionary<string,string> {
            ["__RequestVerificationToken"]=token,["Input.NewPassword"]=replacement,["Input.ConfirmPassword"]=replacement }));
        Assert.Equal(Change,response.Headers.Location!.OriginalString); Assert.True((await State("alice",initial)).PasswordValid);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ExternalSetPasswordValidationIsSafe(bool mismatch)
    {
        using var client=await SignedIn("external");
        var response=await Submit(client,path:Set,next:mismatch?replacement:"weak",confirm:mismatch?Synthetic():"weak");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); var html=await response.Content.ReadAsStringAsync(); NoEcho(html,"weak");
        Assert.Null((await State("external",replacement)).Hash); Capture("set-errors",html);
    }
    [Fact]
    public async Task LimiterBoundsGuessesAcrossTabsAndBothRoutesWithoutBlockingReadsOrOtherUsers()
    {
        using var client=await SignedIn(); using var second=await SignedIn(); using var bob=await SignedIn("bob");
        for(var i=0;i<5;i++) Assert.Equal(HttpStatusCode.OK,(await Submit(i%2==0?client:second,current:Synthetic())).StatusCode);
        var limited=await Submit(second); Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        Assert.Equal("300",limited.Headers.GetValues("Retry-After").Single()); NoEcho(await limited.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(Change)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await client.PostAsync(Set,new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Submit(bob)).StatusCode); Assert.True((await State("alice",initial)).PasswordValid);
    }
    [Fact]
    public async Task OtherCookieSurvivesUntilValidationIntervalThenIsRejectedWhileRefreshedCookieWorks()
    {
        using var current=await SignedIn(); using var other=await SignedIn();
        Assert.Equal(TimeSpan.FromMinutes(30),app.Services.GetRequiredService<IOptions<SecurityStampValidatorOptions>>().Value.ValidationInterval);
        Assert.Equal(HttpStatusCode.Redirect,(await Submit(current)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await other.GetAsync(Change)).StatusCode);
        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Redirect,(await other.GetAsync(Change)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await current.GetAsync(Change)).StatusCode);
    }
    [Fact]
    public async Task ProfileLinkAndPasswordManagerAccessibilityContract()
    {
        using var client=await SignedIn(); var profile=await client.GetStringAsync("/Home/UserProfile");
        Assert.Contains(Change,profile); Assert.Contains("Varnost",profile);
        var response=await client.GetAsync(Change); var html=await response.Content.ReadAsStringAsync();
        Assert.True(response.Headers.CacheControl!.NoStore); NoEcho(html); Capture("change",html);
        foreach(var field in new[]{"CurrentPassword","NewPassword","ConfirmPassword"}) Assert.Contains("for=\"Input_"+field+"\"",html);
        Assert.Contains("autocomplete=\"current-password\"",html);
        Assert.Equal(2,Regex.Matches(html,"autocomplete=\"new-password\"").Count);
        Assert.Contains("aria-describedby=\"current-error\"",html); Assert.Contains("id=\"current-error\"",html);
        using var anonymous=Client(); Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync("/Home/UserProfile")).StatusCode);
    }
    [Fact]
    public async Task ExistingLoginLogoutRegistrationAndAdminAuthorizationRemainAvailable()
    {
        using var anonymous=Client(); Assert.Equal(HttpStatusCode.OK,(await anonymous.GetAsync("/Identity/Account/Register")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await anonymous.GetAsync("/AdminBins/ExportComparison")).StatusCode);
        using var user=await SignedIn(); Assert.Equal(HttpStatusCode.Redirect,(await user.GetAsync("/AdminBins/ExportComparison")).StatusCode);
        var token=Token(await user.GetStringAsync(Change));
        var logout=await user.PostAsync("/Identity/Account/Logout",new FormUrlEncodedContent(new Dictionary<string,string> { ["__RequestVerificationToken"]=token }));
        Assert.Equal(HttpStatusCode.Redirect,logout.StatusCode); Assert.Equal(HttpStatusCode.Redirect,(await user.GetAsync(Change)).StatusCode);
    }
    [Fact]
    public void DefaultPasswordPolicyAndUnknownErrorAreNotReplacedOrLeaked()
    {
        var p=app.Services.GetRequiredService<IOptions<IdentityOptions>>().Value.Password;
        Assert.Equal(6,p.RequiredLength); Assert.Equal(1,p.RequiredUniqueChars);
        Assert.True(p.RequireDigit&&p.RequireLowercase&&p.RequireUppercase&&p.RequireNonAlphanumeric);
        Assert.False(PasswordSettings.Error(new IdentityError { Code="Unknown",Description=replacement }).Contains(replacement));
    }
    public async Task DisposeAsync()
    {
        await app.StopAsync(); await app.DisposeAsync();
        foreach(var suffix in new[]{"","-wal","-shm"}) if(File.Exists(database+suffix)) File.Delete(database+suffix);
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan by)=>now+=by;
    }
    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> lines=new(); public string Text=>string.Join('\n',lines);
        public ILogger CreateLogger(string categoryName)=>new Capture(lines);
        public void Dispose(){}
        private sealed class Capture(ConcurrentQueue<string> lines):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
            public bool IsEnabled(LogLevel logLevel)=>true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter)
                =>lines.Enqueue(formatter(state,exception));
        }
    }
}
