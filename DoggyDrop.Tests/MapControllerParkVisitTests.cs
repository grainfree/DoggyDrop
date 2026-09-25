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
    private const string ParkB = "park-46.0476-14.5515";
    private const string ParkC = "park-46.0615-14.521";
    private const string ParkD = "park-46.0567-14.4965";
    private const string ParkE = "park-46.0519-14.565";
    private const string ParkF = "park-46.0742-14.5148";
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-park-{Guid.NewGuid():N}.db");
    private readonly RecordingNotifications _notifications = new();

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

    [Fact] public async Task FirstDiscovery_SavesRewardsAndAcquiresStamp()
    {
        var dogId = await SeedAsync();
        var json = await VisitAsync(dogId, ParkA);
        Assert.True(json.GetProperty("saved").GetBoolean());
        Assert.True(json.GetProperty("reward").GetProperty("isNewUserDiscovery").GetBoolean());
        Assert.True(json.GetProperty("reward").GetProperty("isNewForDog").GetBoolean());
        Assert.True(json.GetProperty("reward").GetProperty("stamp").GetProperty("isNew").GetBoolean());
        await using var db = Context();
        Assert.Single(await db.DogParkVisits.ToListAsync());
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Single(await db.DogXpEvents.ToListAsync());
    }

    [Fact] public async Task RepeatVisit_IsRevisitAndDoesNotRepeatDiscoveryXp()
    {
        var dogId = await SeedAsync();
        await SeedPriorVisitAndRewardsAsync(dogId, ParkA);
        var json = await VisitAsync(dogId, ParkA);
        Assert.False(json.GetProperty("reward").GetProperty("isNewUserDiscovery").GetBoolean());
        Assert.Equal(2, json.GetProperty("visitCount").GetInt32());
        await using var db = Context();
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Single(await db.DogXpEvents.ToListAsync());
    }

    [Fact] public async Task StampUpgrade_IsShownOnlyWhenThresholdIsCrossed()
    {
        var dogId = await SeedAsync();
        await SeedPriorVisitAndRewardsAsync(dogId, ParkA);
        var stamp = (await VisitAsync(dogId, ParkA)).GetProperty("reward").GetProperty("stamp");
        Assert.True(stamp.GetProperty("wasUpgraded").GetBoolean());
        Assert.Equal("Common", stamp.GetProperty("previousRarity").GetString());
        Assert.Equal("Rare", stamp.GetProperty("rarity").GetString());
    }

    [Fact] public async Task RepeatWithoutRarityChange_DoesNotShowUpgrade()
    {
        var dogId = await SeedAsync();
        const string rarePark = "park-45.5426-13.7184";
        await SeedPriorVisitAndRewardsAsync(dogId, rarePark, area: "Obala");
        var stamp = (await VisitAsync(dogId, rarePark)).GetProperty("reward").GetProperty("stamp");
        Assert.False(stamp.GetProperty("wasUpgraded").GetBoolean());
        Assert.Equal("Rare", stamp.GetProperty("rarity").GetString());
    }

    [Fact] public async Task Discovery_CanLevelUpUser()
    {
        var dogId = await SeedAsync(userXp: 90);
        var userReward = (await VisitAsync(dogId, ParkA)).GetProperty("reward").GetProperty("progression").GetProperty("userReward");
        Assert.True(userReward.GetProperty("leveledUp").GetBoolean());
    }

    [Fact] public async Task Discovery_CanLevelUpDog()
    {
        var dogId = await SeedAsync(dogXp: 70);
        var dogReward = (await VisitAsync(dogId, ParkA)).GetProperty("reward").GetProperty("progression").GetProperty("dogReward");
        Assert.True(dogReward.GetProperty("leveledUp").GetBoolean());
    }

    [Fact] public async Task Discovery_ReturnsExplorerStreakWhenItIncreases()
    {
        var dogId = await SeedAsync(explorerYesterday: true);
        var streak = (await VisitAsync(dogId, ParkA)).GetProperty("reward").GetProperty("progression").GetProperty("streakReward");
        Assert.Equal(2, streak.GetProperty("currentDays").GetInt32());
        Assert.True(streak.GetProperty("increased").GetBoolean());
    }

    [Fact]
    public async Task AcceptedVisit_UsesCapturedVisitInstantAcrossProcessingMidnight()
    {
        var dogId = await SeedAsync();
        var calendar = new TestGamificationCalendar(new DateTime(2026, 7, 10, 21, 59, 59, DateTimeKind.Utc));
        calendar.AdvanceAfterNextRead(new DateTime(2026, 7, 10, 22, 0, 1, DateTimeKind.Utc));
        await using var db = Context();
        var result = Assert.IsType<JsonResult>(await Controller(db, calendar).ParkVisit(Input(dogId, ParkA)));
        Assert.NotNull(result.Value);
        var streak = await db.UserStreaks.SingleAsync(item => item.StreakType == GamificationStreakConstants.Explorer);
        Assert.Equal(new DateOnly(2026, 7, 10), streak.LastActivityDate);
    }

    [Fact] public async Task FifthUniquePark_ReturnsExistingAchievement()
    {
        var dogId = await SeedAsync();
        foreach (var key in new[] { ParkA, ParkB, ParkC, ParkD }) await SeedPriorVisitAndRewardsAsync(dogId, key, includeRewardEvents: false);
        var achievements = (await VisitAsync(dogId, ParkE)).GetProperty("reward").GetProperty("progression").GetProperty("unlockedAchievements");
        Assert.Contains(achievements.EnumerateArray(), item => item.GetProperty("name").GetString() == "Raziskovalec parkov");
        await using var db = Context();
        Assert.Single(await db.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.Explorer5Places).ToListAsync());
    }

    [Fact] public async Task DogOwnedByAnotherUser_IsRejected()
    {
        var dogId = await SeedAsync(ownerId: "someone-else");
        await using var db = Context();
        var result = await Controller(db).ParkVisit(Input(dogId, ParkA));
        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Empty(await db.DogParkVisits.ToListAsync());
    }

    [Fact] public async Task ConcurrentDiscovery_AwardsOnlyOnce()
    {
        var dogId = await SeedAsync();
        var responses = await Task.WhenAll(VisitAsync(dogId, ParkA), VisitAsync(dogId, ParkA));
        Assert.Single(responses, response => response.GetProperty("saved").GetBoolean());
        Assert.Single(responses, response => !response.GetProperty("saved").GetBoolean());
        await using var db = Context();
        Assert.Single(await db.DogParkVisits.ToListAsync());
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Single(await db.DogXpEvents.ToListAsync());
    }

    [Fact]
    public async Task UnknownPlaceKey_IsRejectedWithoutProgression()
    {
        var dogId = await SeedAsync();
        await using var db = Context();
        var result = await Controller(db).ParkVisit(Input(dogId, "park-forged-1-2"));
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.DogParkVisits.ToListAsync());
        Assert.Empty(await db.UserXpEvents.ToListAsync());
        Assert.Empty(await db.DogXpEvents.ToListAsync());
    }

    [Fact]
    public async Task Visit_UsesCanonicalServerMetadata()
    {
        var dogId = await SeedAsync();
        await VisitAsync(dogId, ParkA);
        await using var db = Context();
        var visit = await db.DogParkVisits.SingleAsync();
        var canonical = ParkLocationCatalog.Find(ParkA)!;
        Assert.Equal(canonical.Name, visit.ParkName);
        Assert.Equal(canonical.Area, visit.Area);
        Assert.Equal(canonical.Address, visit.Address);
        Assert.Equal(canonical.Latitude, visit.Latitude);
        Assert.Equal(canonical.Longitude, visit.Longitude);
    }

    [Fact]
    public async Task SecondDogVisit_IsNotNewUserDiscoveryButAwardsDogProgression()
    {
        var dogA = await SeedAsync();
        var dogB = await AddDogAsync("Luna");
        await VisitAsync(dogA, ParkA);
        var second = await VisitAsync(dogB, ParkA);
        var reward = second.GetProperty("reward");
        Assert.False(reward.GetProperty("isNewUserDiscovery").GetBoolean());
        Assert.True(reward.GetProperty("isNewForDog").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reward.GetProperty("progression").GetProperty("userReward").ValueKind);
        Assert.Equal(JsonValueKind.Object, reward.GetProperty("progression").GetProperty("dogReward").ValueKind);
        Assert.False(reward.GetProperty("stamp").GetProperty("isNew").GetBoolean());
        await using var db = Context();
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Equal(2, await db.DogXpEvents.CountAsync());
    }

    [Fact]
    public async Task ExistingVisitHistoryWithoutXpEvent_DoesNotReawardUserDiscoveryXp()
    {
        var dogA = await SeedAsync();
        var dogB = await AddDogAsync("Luna");
        await SeedPriorVisitAndRewardsAsync(dogA, ParkA, includeRewardEvents: false);
        var reward = (await VisitAsync(dogB, ParkA)).GetProperty("reward");
        Assert.False(reward.GetProperty("isNewUserDiscovery").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reward.GetProperty("progression").GetProperty("userReward").ValueKind);
        await using var db = Context();
        Assert.Empty(await db.UserXpEvents.ToListAsync());
        Assert.Single(await db.DogXpEvents.ToListAsync());
    }

    [Fact]
    public async Task TwoDogsConcurrentSameDiscovery_AwardOneUserDiscoveryAndTwoDogRewards()
    {
        var dogA = await SeedAsync();
        var dogB = await AddDogAsync("Luna");
        var results = await Task.WhenAll(VisitAsync(dogA, ParkA), VisitAsync(dogB, ParkA));
        Assert.Single(results, result => result.GetProperty("reward").GetProperty("isNewUserDiscovery").GetBoolean());
        Assert.Single(results, result => result.GetProperty("reward").GetProperty("stamp").GetProperty("isNew").GetBoolean());
        Assert.All(results, result => Assert.True(result.GetProperty("reward").GetProperty("isNewForDog").GetBoolean()));
        await using var db = Context();
        Assert.Equal(2, await db.DogParkVisits.CountAsync());
        Assert.Single(await db.UserXpEvents.ToListAsync());
        Assert.Equal(2, await db.DogXpEvents.CountAsync());
    }

    [Fact]
    public async Task MultiDogVisits_ContributeToOneUserStamp()
    {
        var dogA = await SeedAsync();
        var dogB = await AddDogAsync("Luna");
        await VisitAsync(dogA, ParkA);
        var stamp = (await VisitAsync(dogB, ParkA)).GetProperty("reward").GetProperty("stamp");
        Assert.True(stamp.GetProperty("wasUpgraded").GetBoolean());
        Assert.Equal("Rare", stamp.GetProperty("rarity").GetString());
    }

    [Fact]
    public async Task ConcurrentFifthAndSixthParks_UnlockAchievementOnce()
    {
        var dogA = await SeedAsync();
        var dogB = await AddDogAsync("Luna");
        foreach (var key in new[] { ParkA, ParkB, ParkC, ParkD }) await SeedPriorVisitAndRewardsAsync(dogA, key, includeRewardEvents: false);
        var results = await Task.WhenAll(VisitAsync(dogA, ParkE), VisitAsync(dogB, ParkF));
        var unlocks = results.Sum(result => result.GetProperty("reward").GetProperty("progression")
            .GetProperty("unlockedAchievements").GetArrayLength());
        Assert.Equal(1, unlocks);
        Assert.Equal(1, _notifications.Count($"Achievement:{UserAchievementCatalog.Explorer5Places}", "Raziskovalec parkov"));
        await using var db = Context();
        Assert.Single(await db.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.Explorer5Places).ToListAsync());
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

    private async Task<int> AddDogAsync(string name)
    {
        await using var db = Context();
        var dog = new Dog { Name = name, OwnerId = UserId };
        db.Dogs.Add(dog);
        await db.SaveChangesAsync();
        return dog.Id;
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

    private async Task<JsonElement> VisitAsync(int dogId, string key)
    {
        await using var db = Context();
        var result = Assert.IsType<JsonResult>(await Controller(db).ParkVisit(Input(dogId, key)));
        return JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
