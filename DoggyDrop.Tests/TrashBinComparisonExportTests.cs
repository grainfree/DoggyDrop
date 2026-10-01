using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class TrashBinComparisonExportTests : IAsyncLifetime
{
    private const string Header = "BinId,Latitude,Longitude,IsApproved,DateAdded,ApprovedAt,DataSourceId";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-bin-export-{Guid.NewGuid():N}.db");
    private readonly ReadOnlyGuard guard = new();
    private readonly NoSaveGuard saves = new();
    private WebApplication app = null!;

    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop", "DoggyDrop.csproj"))) root = root.Parent;
        Assert.NotNull(root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AdminBinsController).Assembly.GetName().Name,
            ContentRootPath = Path.Combine(root.FullName, "DoggyDrop"), EnvironmentName = "Testing", Args = []
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite($"Data Source={database};Pooling=False").AddInterceptors(guard, saves));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; })
            .AddScheme<AuthenticationSchemeOptions, TestUser>("Test", _ => { });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(AdminBinsController).Assembly);
        builder.Services.AddRazorPages().AddApplicationPart(typeof(AdminBinsController).Assembly);
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(context.Request.Headers["X-Test-Culture"].FirstOrDefault() ?? "sl-SI");
            await next(context);
        });
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Map}/{action=Index}/{id?}"); app.MapRazorPages();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "PRIVATE-USER", UserName = "PRIVATE-USERNAME", Email = "private@example.invalid" });
            db.DataSources.Add(new DataSource { Id = 7, Name = "PRIVATE-SOURCE", ContactName = "PRIVATE-CONTACT", ContactEmail = "source@example.invalid", Notes = "PRIVATE-NOTES" });
            db.TrashBins.AddRange(
                new TrashBin { Id = 20, Name = "=HYPERLINK(\"private\")\r\nPRIVATE-NAME", Latitude = 46.123456, Longitude = 14.987654,
                    IsApproved = true, DateAdded = new DateTime(2026, 9, 28, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567),
                    ApprovedAt = new DateTime(2026, 9, 29, 1, 2, 3, DateTimeKind.Utc), DataSourceId = 7, UserId = "PRIVATE-USER",
                    ImageUrl = "https://photos.example.invalid/PRIVATE-PHOTO", UsedCount = 999, FullReports = 888, MissingReports = 777,
                    UsefulVotes = 666, NotUsefulVotes = 555, LastUsedAt = new DateTime(2026, 1, 1), LastReportedAt = new DateTime(2026, 2, 1) },
                new TrashBin { Id = 3, Name = "PRIVATE-PENDING", Latitude = 0, Longitude = -15.25, IsApproved = false,
                    DateAdded = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }
        guard.Armed = saves.Armed = true;
        guard.Commands.Clear();
        await app.StartAsync();
    }

    private HttpClient Client(string? user = "admin", string culture = "sl-SI")
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        if (user != null) client.DefaultRequestHeaders.Add("X-Test-Role", user);
        client.DefaultRequestHeaders.Add("X-Test-Culture", culture);
        return client;
    }
    private static string[] Lines(byte[] bytes)
    {
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes[..3]);
        return new UTF8Encoding(false, true).GetString(bytes[3..]).Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("user", HttpStatusCode.Forbidden)]
    public async Task DownloadRequiresAdmin(string? role, HttpStatusCode expected)
    {
        using var client = Client(role);
        var response = await client.GetAsync("/AdminBins/ExportComparison");
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.DoesNotContain(Header, await response.Content.ReadAsStringAsync());
        Assert.Empty(guard.Commands);
    }

    [Theory]
    [InlineData("sl-SI")]
    [InlineData("en-US")]
    public async Task AdminCsvUsesExactAllowlistInvariantNumbersUtcDatesAndBlankNulls(string culture)
    {
        using var client = Client(culture: culture);
        var before = DateTime.UtcNow.AddSeconds(-1);
        var response = await client.GetAsync("/AdminBins/ExportComparison?state=approved&sourceId=7&page=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        var filename = (disposition.FileNameStar ?? disposition.FileName)!.Trim('"');
        Assert.Matches(@"^doggydrop-trashbins-\d{8}T\d{6}Z\.csv$", filename);
        var time = DateTime.ParseExact(filename[20..^4], "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        Assert.InRange(time, before, DateTime.UtcNow);
        var bytes = await response.Content.ReadAsByteArrayAsync(); var lines = Lines(bytes);
        Assert.Equal(new[] { Header,
            "3,0,-15.25,false,2026-08-01T00:00:00.0000000Z,,",
            "20,46.123456,14.987654,true,2026-09-28T12:34:56.1234567Z,2026-09-29T01:02:03.0000000Z,7" }, lines);
        Assert.All(lines, line => Assert.Equal(7, line.Split(',').Length));
        var body = Encoding.UTF8.GetString(bytes);
        foreach (var forbidden in new[] { "PRIVATE", "example.invalid", "UserId", "Name", "ImageUrl", "Reports", "UsedCount", "LastUsedAt", "LastReportedAt", "Votes", "Contact", "Notes", "Email", "HYPERLINK" })
            Assert.DoesNotContain(forbidden, body);
        var sql = Assert.Single(guard.Commands);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var forbidden in new[] { "UserId", "Name", "ImageUrl", "Reports", "UsedCount", "LastUsedAt", "LastReportedAt", "Votes" })
            Assert.DoesNotContain(forbidden, sql);
        var cache = response.Headers.CacheControl!;
        Assert.True(cache.NoStore); Assert.True(cache.NoCache); Assert.True(cache.Private); Assert.False(cache.Public);
        Assert.Contains(response.Headers.Pragma, p => p.Name == "no-cache");
    }

    [Fact]
    public async Task ExportDoesNotTrackWriteOrMutateData()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var before = JsonSerializer.Serialize(await db.TrashBins.AsNoTracking().OrderBy(b => b.Id).ToListAsync());
        var controller = new AdminBinsController(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<FileContentResult>(await controller.ExportComparison(CancellationToken.None));
        Assert.Empty(db.ChangeTracker.Entries());
        var after = JsonSerializer.Serialize(await db.TrashBins.AsNoTracking().OrderBy(b => b.Id).ToListAsync());
        Assert.Equal(before, after);
        Assert.Equal(0, saves.Attempts);
    }

    [Fact]
    public async Task EmptyDatabaseProducesOnlyHeader()
    {
        await using var scope = app.Services.CreateAsyncScope();
        guard.Armed = false;
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.ExecuteDeleteAsync();
        guard.Armed = true;
        using var client = Client();
        Assert.Equal(new[] { Header }, Lines(await client.GetByteArrayAsync("/AdminBins/ExportComparison")));
    }

    [Fact]
    public async Task ExportIncludesRowsBeyondTheAdminPageSize()
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        guard.Armed = saves.Armed = false;
        db.TrashBins.AddRange(Enumerable.Range(100, 101).Select(id => new TrashBin { Id = id, Name = "PRIVATE", Latitude = 46, Longitude = 15 }));
        await db.SaveChangesAsync(); guard.Armed = saves.Armed = true;
        using var client = Client(); var lines = Lines(await client.GetByteArrayAsync("/AdminBins/ExportComparison"));
        Assert.Equal(104, lines.Length);
        Assert.Equal(new[] { 3, 20 }.Concat(Enumerable.Range(100, 101)), lines.Skip(1).Select(line => int.Parse(line.Split(',')[0], CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task ExistingAdminPageOffersDownloadAndExplainsUnfilteredScope()
    {
        using var client = Client(); var html = await client.GetStringAsync("/AdminBins?state=pending");
        Assert.Contains("href=\"/AdminBins/ExportComparison\"", html);
        var text = WebUtility.HtmlDecode(html);
        Assert.Contains("Izvozi koše za primerjavo", text);
        Assert.Contains("CSV vsebuje samo infrastrukturne podatke za preverjanje dvojnikov in virov.", text);
        Assert.Contains("ne glede na spodnje filtre", text);
        var output = Environment.GetEnvironmentVariable("DOGGYDROP_BIN_EXPORT_REVIEW_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) { Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "admin-bins.html"), html); }
    }

    public async Task DisposeAsync() { if (app != null) await app.DisposeAsync(); if (File.Exists(database)) File.Delete(database); }

    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].ToString();
            if (role is not ("admin" or "user")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "PRIVATE-USER"), new Claim(ClaimTypes.Name, "Test")], IdentityConstants.ApplicationScheme);
            if (role == "admin") identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
    private sealed class NoSaveGuard : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }
        private void Check() { if (Armed) { Attempts++; throw new InvalidOperationException("Export attempted SaveChanges"); } }
        public override InterceptionResult<int> SavingChanges(DbContextEventData data, InterceptionResult<int> result) { Check(); return result; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { Check(); return ValueTask.FromResult(result); }
    }
    private sealed class ReadOnlyGuard : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public List<string> Commands { get; } = [];
        private void Check(DbCommand command)
        {
            if (!Armed) return;
            if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Export attempted a non-SELECT command");
            Commands.Add(command.CommandText);
        }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r) { Check(c); return r; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct = default) { Check(c); return ValueTask.FromResult(r); }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r) { Check(c); return r; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<int> r, CancellationToken ct = default) { Check(c); return ValueTask.FromResult(r); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r) { Check(c); return r; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e, InterceptionResult<object> r, CancellationToken ct = default) { Check(c); return ValueTask.FromResult(r); }
    }
}
