using System.Security.Claims;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class MapControllerParkVisitTests : IDisposable
{
    private const string UserId = "park-user";
    private const string ParkA = "park-46.0689-14.4697";
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-park-{Guid.NewGuid():N}.db");
    private readonly RecordingNotifications _notifications = new();

    [Fact]
    public async Task AddBinGet_IsPublicAndReturnsFormForAnonymousVisitor()
    {
        await using var db = Context();
        var controller = Controller(db);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        controller.Request.Method = HttpMethods.Get;
        controller.Request.Path = "/Map/Add";

        var action = typeof(MapController).GetMethod(nameof(MapController.Add), [typeof(int?)]);
        Assert.NotNull(action);
        Assert.Empty(typeof(MapController).GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
        Assert.Empty(action.GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
        Assert.False(controller.User.Identity!.IsAuthenticated);
        Assert.IsType<ViewResult>(await controller.Add());
        Assert.Null((int?)controller.ViewBag.WalkId);
    }

    [Fact]
    public async Task AddBinPost_AnonymousInvalidInputReturnsValidationWithoutWriting()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var controller = Controller(db);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        controller.Request.Method = HttpMethods.Post;
        controller.Request.Path = "/Map/Add";
        var input = new TrashBinViewModel();
        // Direct controller tests supply the ModelState that MVC validation produces.
        controller.ModelState.AddModelError(nameof(TrashBinViewModel.Name), "Ime je obvezno.");

        var action = typeof(MapController).GetMethod(nameof(MapController.Add),
            [typeof(TrashBinViewModel), typeof(int?)]);
        Assert.NotNull(action);
        Assert.Empty(typeof(MapController).GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
        Assert.Empty(action.GetCustomAttributes(inherit: true)
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>());
        Assert.Single(action.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true));
        Assert.Single(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true));
        Assert.False(controller.User.Identity!.IsAuthenticated);

        var result = Assert.IsType<ViewResult>(await controller.Add(input));
        Assert.Same(input, result.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Single(controller.ModelState[nameof(TrashBinViewModel.Name)]!.Errors);
        Assert.False(await db.TrashBins.AnyAsync());
    }

    [Fact]
    public async Task PublicBinList_ContainsOnlyApprovedBinsForActiveWalk()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.TrashBins.AddRange(
            new TrashBin { Name = "Approved", Latitude = 46.05, Longitude = 14.51, IsApproved = true },
            new TrashBin { Name = "Pending", Latitude = 46.06, Longitude = 14.52, IsApproved = false });
        await db.SaveChangesAsync();

        var response = Assert.IsType<JsonResult>(Controller(db).FindNearest());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var bin = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("Approved", bin.GetProperty("Name").GetString());
        Assert.Equal(46.05, bin.GetProperty("Latitude").GetDouble());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HomeOnboardingDependsOnlyOnOwnedDogs(int ownedDogCount)
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = UserId, UserName = "park@test" },
            new ApplicationUser { Id = "someone-else", UserName = "other@test" });
        db.Dogs.Add(new Dog { Name = "Foreign dog", OwnerId = "someone-else" });
        for (var i = 0; i < ownedDogCount; i++)
        {
            db.Dogs.Add(new Dog { Name = $"Owned dog {i}", OwnerId = UserId });
        }
        await db.SaveChangesAsync();

        var controller = Controller(db);
        Assert.IsType<ViewResult>(await controller.Index());
        Assert.Equal(ownedDogCount == 0, (bool)controller.ViewBag.NeedsDogOnboarding);
        var quickStartDogId = (int?)controller.ViewBag.QuickStartDogId;
        Assert.Equal(ownedDogCount == 1, quickStartDogId.HasValue);
    }

    [Fact]
    public async Task AdminCreatedApprovedBin_NotifiesNearbyUserButNotContributor()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = UserId, UserName = "admin@test" },
            new ApplicationUser { Id = "nearby", UserName = "nearby@test" });
        db.NearbyDiscoveryPreferences.AddRange(
            new NearbyDiscoveryPreference
            {
                UserId = UserId, Latitude = 46.05, Longitude = 14.51,
                RadiusMeters = 1000, BinsEnabled = true, EnabledAt = DateTime.UtcNow.AddDays(-1)
            },
            new NearbyDiscoveryPreference
            {
                UserId = "nearby", Latitude = 46.05, Longitude = 14.51,
                RadiusMeters = 1000, BinsEnabled = true, EnabledAt = DateTime.UtcNow.AddDays(-1)
            });
        await db.SaveChangesAsync();

        var result = await Controller(db, admin: true).Add(new TrashBinViewModel
        {
            Name = "Immediate approval", Latitude = 46.05, Longitude = 14.51
        });

        Assert.IsType<RedirectToActionResult>(result);
        var bin = await db.TrashBins.AsNoTracking().SingleAsync();
        Assert.True(bin.IsApproved);
        Assert.NotNull(bin.ApprovedAt);
        var notification = await db.UserNotifications.AsNoTracking().SingleAsync();
        Assert.Equal("nearby", notification.UserId);
        Assert.Equal("NewBinNearby", notification.Type);
        Assert.Equal($"Bin:{bin.Id}", notification.SourceKey);
    }

    [Fact]
    public void ParkVisit_RequiresAntiforgeryValidation()
    {
        var action = typeof(MapController).GetMethod(nameof(MapController.ParkVisit));

        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public void Reject_RequiresAntiforgeryValidation()
    {
        var action = typeof(MapController).GetMethod(nameof(MapController.Reject));
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public async Task Reject_OwnerCannotRemovePendingProposalOrChangeXp()
    {
        var binId = await AddBinAsync();
        await using var db = Context();
        db.UserXpEvents.Add(new UserXpEvent { UserId = UserId, ActivityType = GamificationConstants.AddedTrashBin, XpAmount = GamificationConstants.AddedTrashBinXp, ReferenceType = nameof(TrashBin), ReferenceId = binId.ToString() });
        await db.UserGamificationProfiles.Where(profile => profile.UserId == UserId)
            .ExecuteUpdateAsync(update => update.SetProperty(profile => profile.TotalXp, GamificationConstants.AddedTrashBinXp));
        await db.SaveChangesAsync();

        var controller = Controller(db);
        Assert.IsType<RedirectToActionResult>(await controller.Reject(binId, "mybins"));
        Assert.NotNull(controller.TempData["ErrorMessage"]);
        Assert.True(await db.TrashBins.AnyAsync(bin => bin.Id == binId && !bin.IsApproved));
        Assert.Equal(GamificationConstants.AddedTrashBinXp, await db.UserGamificationProfiles.Where(profile => profile.UserId == UserId).Select(profile => profile.TotalXp).SingleAsync());
        Assert.Single(await db.UserXpEvents.Where(xp => xp.ReferenceId == binId.ToString()).ToListAsync());
    }

    [Fact]
    public async Task Reject_OwnerCannotRemoveApprovedPublicBin()
    {
        var binId = await AddBinAsync(approved: true);
        await using var db = Context();
        var controller = Controller(db);
        Assert.IsType<RedirectToActionResult>(await controller.Reject(binId, "mybins"));
        Assert.NotNull(controller.TempData["ErrorMessage"]);
        Assert.True(await db.TrashBins.AnyAsync(bin => bin.Id == binId && bin.IsApproved));
    }

    [Fact]
    public async Task Reject_ForeignUserCannotRemoveProposal()
    {
        var binId = await AddBinAsync(ownerId: "someone-else");
        await using var db = Context();
        Assert.IsType<NotFoundResult>(await Controller(db).Reject(binId, "mybins"));
        Assert.True(await db.TrashBins.AnyAsync(bin => bin.Id == binId));
    }

    [Fact]
    public async Task Reject_NonexistentBinReturnsSameResultAsForeignBin()
    {
        await SeedAsync();
        await using var db = Context();
        Assert.IsType<NotFoundResult>(await Controller(db).Reject(99999, "mybins"));
    }

    [Fact]
    public async Task Reject_AdminCanStillModerateApprovedBin()
    {
        var binId = await AddBinAsync(approved: true, ownerId: "someone-else");
        await using var db = Context();
        Assert.IsType<RedirectToActionResult>(await Controller(db, admin: true).Reject(binId, null));
        Assert.False(await db.TrashBins.AnyAsync(bin => bin.Id == binId));
    }

    public static IEnumerable<object[]> RetiredKeys => ParkLocationCatalog.All
        .Select(park => new object[] { park.PlaceKey }).Concat(new[] { new object[] { "unknown-key" }, new object[] { "" } });

    [Theory]
    [MemberData(nameof(RetiredKeys))]
    public async Task RetiredCheckInReturnsGoneWithoutChangingHistoryOrRewards(string key)
    {
        var dogId = await SeedAsync(userXp: 90, dogXp: 70);
        await SeedPriorVisitAndRewardsAsync(dogId, ParkA);
        await using var db = Context();
        var visits = await db.DogParkVisits.AsNoTracking().ToListAsync();
        var result = Assert.IsType<ObjectResult>(await Controller(db).ParkVisit(Input(dogId, key)));
        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
        Assert.Equal(visits.Select(v => v.Id), await db.DogParkVisits.Select(v => v.Id).ToListAsync());
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Single(await db.DogXpEvents.ToListAsync());
        Assert.Equal(90, (await db.UserGamificationProfiles.SingleAsync()).TotalXp);
        Assert.Equal(70, (await db.DogProgressionProfiles.SingleAsync()).TotalXp);
        Assert.Empty(await db.UserAchievements.ToListAsync());
        Assert.Empty(await db.UserStreaks.ToListAsync());
    }

    private async Task<int> SeedAsync(string ownerId = UserId, int userXp = 0, int dogXp = 0, bool explorerYesterday = false)
    {
        await using var db = Context();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = UserId, UserName = "park@test" }, new ApplicationUser { Id = "someone-else", UserName = "other@test" });
        var dog = new Dog { Name = "Floyd", OwnerId = ownerId };
        db.Dogs.Add(dog);
        await db.SaveChangesAsync();
        db.UserGamificationProfiles.Add(new UserGamificationProfile { UserId = UserId, TotalXp = userXp, Level = 1 });
        if (dogXp > 0) db.DogProgressionProfiles.Add(new DogProgressionProfile { DogId = dog.Id, TotalXp = dogXp, Level = 1 });
        if (explorerYesterday) db.UserStreaks.Add(new UserStreak { UserId = UserId, StreakType = GamificationStreakConstants.Explorer, CurrentDays = 1, LongestDays = 1, LastActivityDate = new TestGamificationCalendar().Today.AddDays(-1) });
        await db.SaveChangesAsync();
        return dog.Id;
    }

    private async Task<int> AddBinAsync(bool approved = false, string ownerId = UserId)
    {
        await SeedAsync();
        await using var db = Context();
        var bin = new TrashBin { Name = "Testni koš", UserId = ownerId, IsApproved = approved, Latitude = 46, Longitude = 15 };
        db.TrashBins.Add(bin);
        await db.SaveChangesAsync();
        return bin.Id;
    }

    private async Task SeedPriorVisitAndRewardsAsync(int dogId, string key, string? area = null, bool includeRewardEvents = true)
    {
        await using var db = Context();
        db.DogParkVisits.Add(new DogParkVisit { DogId = dogId, UserId = UserId, ParkName = key, PlaceKey = key, Area = area, Latitude = 46, Longitude = 15, VisitedAt = DateTime.UtcNow.AddHours(-3) });
        if (includeRewardEvents)
        {
            db.UserXpEvents.Add(new UserXpEvent { UserId = UserId, ActivityType = GamificationConstants.VisitNewPark, XpAmount = 40, ReferenceType = nameof(DogParkVisit), ReferenceId = $"{dogId}:{key}" });
            db.DogXpEvents.Add(new DogXpEvent { DogId = dogId, ActivityType = "ParkVisit", XpAmount = 35, ReferenceType = nameof(DogParkVisit), ReferenceId = $"{dogId}:{key}" });
        }
        await db.SaveChangesAsync();
    }

    private static ParkVisitInput Input(int dogId, string key) => new() { DogId = dogId, PlaceKey = key };
    private ApplicationDbContext Context()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False");
        return new ApplicationDbContext(options.Options);
    }

    private MapController Controller(ApplicationDbContext db, TestGamificationCalendar? calendar = null, bool admin = false)
    {
        calendar ??= new TestGamificationCalendar();
        var controller = new MapController(db, new TestEnvironment(), UserManager(db), new NoOpImages(), new NoOpEmail(), _notifications,
            new GamificationService(db, _notifications, calendar), new DogProgressionService(db), new MapStampService(), new GamificationRewardBuilder(), calendar,
            new UserAchievementService(db, _notifications));
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) } };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempDataProvider());
        controller.Url = new StubUrl();
        return controller;
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static UserManager<ApplicationUser> UserManager(ApplicationDbContext db) => new(new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db), Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
    public void Dispose() { }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly HashSet<string> _items = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null)
        {
            lock (_gate) _items.Add($"{userId}|{type}|{title}");
            return Task.CompletedTask;
        }
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) =>
            CreateAsync(userId, type, title, body, linkUrl);
        public int Count(string type, string title)
        {
            lock (_gate) return _items.Count(item => item.EndsWith($"|{type}|{title}", StringComparison.Ordinal));
        }
    }
    private sealed class NoOpImages : ICloudinaryService { public Task<string?> UploadImageAsync(IFormFile f)=>Task.FromResult<string?>(null); public Task<string?> UploadTrashBinImageAsync(IFormFile f)=>Task.FromResult<string?>(null); public Task<string?> UploadWalkImageAsync(IFormFile f)=>Task.FromResult<string?>(null); }
    private sealed class NoOpEmail : IEmailSender { public Task SendEmailAsync(string e,string s,string h)=>Task.CompletedTask; }
    private sealed class TestEnvironment : IWebHostEnvironment { public string ApplicationName {get;set;}="Tests"; public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider(); public string WebRootPath {get;set;}=""; public string EnvironmentName {get;set;}="Development"; public string ContentRootPath {get;set;}=""; public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider(); }
    private sealed class StubUrl : IUrlHelper { public ActionContext ActionContext {get;}=new(); public string? Action(UrlActionContext c)=>"/Map"; public string? Content(string? p)=>p; public bool IsLocalUrl(string? u)=>true; public string? Link(string? r,object? v)=>"/Map"; public string? RouteUrl(UrlRouteContext c)=>"/Map"; }
}
