using System.Security.Claims;
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
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class AchievementControllerIntegrationTests : IDisposable
{
    private const string UserId = "controller-achievement-user";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"doggydrop-controller-achievements-{Guid.NewGuid():N}.db");

    public AchievementControllerIntegrationTests()
    {
        using var context = Context();
        context.Database.EnsureCreated();
        context.Users.Add(new ApplicationUser { Id = UserId, UserName = "controller@example.test", NormalizedUserName = "CONTROLLER@EXAMPLE.TEST" });
        context.SaveChanges();
    }

    [Fact]
    public async Task DogsDashboard_WithoutDogsIsAnEmptyOnboardingModel()
    {
        await using var context = Context();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var model = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.Empty(model.Dogs);
        Assert.Null(model.SelectedDog);
        Assert.Empty(model.RecentWalks);
        Assert.Empty(model.RecentPhotos);
        Assert.Null(model.Level);
    }

    [Fact]
    public async Task DogsDashboard_OneDogIsSelectedWithoutQueryParameter()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        context.DogProgressionProfiles.Add(new DogProgressionProfile { DogId = dog.Id, TotalXp = 90, Adventure = 4 });
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var model = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.Single(model.Dogs);
        Assert.Equal(dog.Id, model.SelectedDog?.Id);
        Assert.Equal(90, model.Level?.TotalXp);
        Assert.Equal(4, model.Progression?.Adventure);
        Assert.Null(model.ActiveWalkId);
    }

    [Fact]
    public async Task DogsDashboard_OnlyInterruptedWalksShowNoCompletedActivity()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        var walk = new Walk { OwnerId = UserId, DogId = dog.Id, Status = "Interrupted", DistanceMeters = 9000, EndedAt = DateTime.UtcNow };
        context.Walks.Add(walk);
        await context.SaveChangesAsync();
        context.WalkPhotos.Add(new WalkPhoto { WalkId = walk.Id, UserId = UserId, ImageUrl = "https://example.test/interrupted.jpg" });
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var model = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);

        Assert.Equal(0, model.CompletedWalkCount);
        Assert.Equal(0, model.TotalDistanceKm);
        Assert.Empty(model.RecentWalks);
        Assert.Empty(model.RecentPhotos);
    }

    [Fact]
    public async Task DogsDashboard_ShowsOnlySelectedOwnedDogsCompletedWalksAndPhotos()
    {
        await using var context = Context();
        var otherId = "other-dog-owner";
        context.Users.Add(new ApplicationUser { Id = otherId, UserName = "other@example.test", NormalizedUserName = "OTHER@EXAMPLE.TEST" });
        var mine = new Dog { Name = "Luna", OwnerId = UserId };
        var another = new Dog { Name = "Rex", OwnerId = UserId };
        var others = new Dog { Name = "Other", OwnerId = otherId };
        context.Dogs.AddRange(mine, another, others);
        await context.SaveChangesAsync();
        var completed = new Walk { DogId = mine.Id, OwnerId = UserId, Status = "Completed", DistanceMeters = 2400, EndedAt = DateTime.UtcNow };
        var interrupted = new Walk { DogId = mine.Id, OwnerId = UserId, Status = "Interrupted", DistanceMeters = 9000, EndedAt = DateTime.UtcNow };
        var anotherWalk = new Walk { DogId = another.Id, OwnerId = UserId, Status = "Completed", DistanceMeters = 5500, EndedAt = DateTime.UtcNow };
        var otherWalk = new Walk { DogId = others.Id, OwnerId = otherId, Status = "Completed", DistanceMeters = 1000, EndedAt = DateTime.UtcNow };
        context.Walks.AddRange(completed, interrupted, anotherWalk, otherWalk);
        await context.SaveChangesAsync();
        context.WalkPhotos.AddRange(
            new WalkPhoto { WalkId = completed.Id, UserId = UserId, ImageUrl = "https://example.test/mine.jpg" },
            new WalkPhoto { WalkId = interrupted.Id, UserId = UserId, ImageUrl = "https://example.test/interrupted.jpg" },
            new WalkPhoto { WalkId = anotherWalk.Id, UserId = UserId, ImageUrl = "https://example.test/another.jpg" },
            new WalkPhoto { WalkId = otherWalk.Id, UserId = otherId, ImageUrl = "https://example.test/other.jpg" });
        context.DogParkVisits.AddRange(
            new DogParkVisit { DogId = mine.Id, UserId = UserId, PlaceKey = "park-a", ParkName = "Park A" },
            new DogParkVisit { DogId = mine.Id, UserId = UserId, PlaceKey = "park-a", ParkName = "Park A" },
            new DogParkVisit { DogId = another.Id, UserId = UserId, PlaceKey = "park-b", ParkName = "Park B" });
        await context.SaveChangesAsync();

        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));
        var result = Assert.IsType<ViewResult>(await controller.Index(mine.Id));
        var dashboard = Assert.IsType<DogsDashboardViewModel>(result.Model);
        Assert.Equal(mine.Id, dashboard.SelectedDog?.Id);
        Assert.Equal(2, dashboard.Dogs.Count);
        Assert.Equal(1, dashboard.CompletedWalkCount);
        Assert.Equal(2.4, dashboard.TotalDistanceKm, 3);
        Assert.Equal(1, dashboard.ParkLocationCount);
        Assert.Single(dashboard.RecentWalks);
        Assert.Single(dashboard.RecentPhotos);
        Assert.Equal(completed.Id, dashboard.RecentPhotos[0].WalkId);
        Assert.DoesNotContain(dashboard.RecentPhotos, photo => photo.WalkId == anotherWalk.Id);
        Assert.DoesNotContain(dashboard.RecentWalks, walk => walk.Id == interrupted.Id);
    }

    [Fact]
    public async Task DogsDashboard_RejectsForeignDogSelectionAndDetails()
    {
        await using var context = Context();
        var otherUser = new ApplicationUser { Id = "foreign-user", UserName = "foreign@example.test" };
        var otherDog = new Dog { Name = "Tuj pes", OwnerId = otherUser.Id };
        context.Users.Add(otherUser);
        context.Dogs.Add(otherDog);
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        Assert.IsType<NotFoundResult>(await controller.Index(otherDog.Id));
        Assert.IsType<NotFoundResult>(await controller.Details(otherDog.Id));
    }

    [Fact]
    public async Task DogsDashboard_BoundsRecentWalksAndPhotosToSelectedDog()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 9; i++)
        {
            var walk = new Walk { OwnerId = UserId, DogId = dog.Id, Status = "Completed", DistanceMeters = 100 + i, StartedAt = now.AddDays(-i), EndedAt = now.AddDays(-i) };
            context.Walks.Add(walk);
            await context.SaveChangesAsync();
            context.WalkPhotos.Add(new WalkPhoto { WalkId = walk.Id, UserId = UserId, ImageUrl = $"https://example.test/{i}.jpg", CreatedAt = now.AddDays(-i) });
        }
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var model = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index(dog.Id)).Model);

        Assert.Equal(9, model.CompletedWalkCount);
        Assert.Equal(3, model.RecentWalks.Count);
        Assert.Equal(6, model.RecentPhotos.Count);
        Assert.Equal("https://example.test/0.jpg", model.RecentPhotos[0].ImageUrl);
    }

    [Fact]
    public async Task DogsDashboard_OffersExistingActiveWalkInsteadOfAnotherStart()
    {
        await using var context = Context();
        var selected = new Dog { Name = "Luna", OwnerId = UserId };
        var walking = new Dog { Name = "Rex", OwnerId = UserId };
        context.Dogs.AddRange(selected, walking);
        await context.SaveChangesAsync();
        var active = new Walk { OwnerId = UserId, DogId = walking.Id, Status = "Active", StartedAt = DateTime.UtcNow };
        context.Walks.Add(active);
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var model = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index(selected.Id)).Model);

        Assert.Equal(selected.Id, model.SelectedDog?.Id);
        Assert.Equal(active.Id, model.ActiveWalkId);
        Assert.Equal("Rex", model.ActiveWalkDogName);
    }

    [Fact]
    public async Task DogDetails_AndDashboardCountDistinctVisitedParks()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        var another = new Dog { Name = "Rex", OwnerId = UserId };
        context.Dogs.AddRange(dog, another);
        await context.SaveChangesAsync();
        context.DogParkVisits.AddRange(
            new DogParkVisit { DogId = dog.Id, UserId = UserId, PlaceKey = "park-a", ParkName = "Park A" },
            new DogParkVisit { DogId = dog.Id, UserId = UserId, PlaceKey = "park-a", ParkName = "Park A" },
            new DogParkVisit { DogId = another.Id, UserId = UserId, PlaceKey = "park-b", ParkName = "Park B" });
        await context.SaveChangesAsync();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, new NoOpNotifications())));

        var dashboard = Assert.IsType<DogsDashboardViewModel>(Assert.IsType<ViewResult>(await controller.Index(dog.Id)).Model);
        var details = Assert.IsType<DogDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(dog.Id)).Model);

        Assert.Equal(1, dashboard.ParkLocationCount);
        Assert.Equal(1, details.ParkLocationCount);
    }

    [Fact]
    public async Task FirstDogCreation_UnlocksDogParentPermanently()
    {
        await using var context = Context();
        var notifications = new NoOpNotifications();
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, notifications)));

        await controller.Create(new DogCreateViewModel { Name = "Luna", IsFirstDog = true });

        var achievement = await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.DogParent);
        var dog = await context.Dogs.SingleAsync();
        context.Dogs.Remove(dog);
        await context.SaveChangesAsync();
        Assert.NotNull(await context.UserAchievements.FindAsync(achievement.Id));
    }

    [Fact]
    public async Task BinSubmissionAchievements_DoNotRequireApproval()
    {
        await using var context = Context();
        var notifications = new NoOpNotifications();
        var calendar = new TestGamificationCalendar();
        var achievementService = new UserAchievementService(context, notifications);
        var controller = Prepare(new MapController(
            context, new TestEnvironment(), UserManager(context), new NoOpImages(), new NoOpEmail(), notifications,
            new GamificationService(context, notifications, calendar), new DogProgressionService(context), new MapStampService(),
            new GamificationRewardBuilder(), calendar, achievementService));

        for (var i = 1; i <= 10; i++)
            await controller.Add(new TrashBinViewModel { Name = $"Predlog {i}", Latitude = 46.05 + i / 1000d, Longitude = 14.5 });

        Assert.Equal(10, await context.TrashBins.CountAsync(item => item.UserId == UserId && !item.IsApproved));
        Assert.True(await achievementService.IsOwnedAsync(UserId, UserAchievementCatalog.BinFirstSubmission));
        Assert.True(await achievementService.IsOwnedAsync(UserId, UserAchievementCatalog.Bin10Submissions));
    }

    [Fact]
    public async Task BinSubmission_ReturnsToOwnedActiveWalkOnly()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        var walk = new Walk { DogId = dog.Id, OwnerId = UserId, Status = "Active" };
        context.Walks.Add(walk);
        await context.SaveChangesAsync();
        var notifications = new NoOpNotifications();
        var calendar = new TestGamificationCalendar();
        var controller = Prepare(new MapController(
            context, new TestEnvironment(), UserManager(context), new NoOpImages(), new NoOpEmail(), notifications,
            new GamificationService(context, notifications, calendar), new DogProgressionService(context), new MapStampService(),
            new GamificationRewardBuilder(), calendar, new UserAchievementService(context, notifications)));

        var ownedResult = await controller.Add(new TrashBinViewModel { Name = "Koš A", Latitude = 46.05, Longitude = 14.5 }, walk.Id);
        var unrelatedResult = await controller.Add(new TrashBinViewModel { Name = "Koš B", Latitude = 46.06, Longitude = 14.5 }, int.MaxValue);

        Assert.Equal("Active", Assert.IsType<RedirectToActionResult>(ownedResult).ActionName);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(unrelatedResult).ActionName);
        Assert.Equal("Active", (await context.Walks.SingleAsync(item => item.Id == walk.Id)).Status);
    }

    [Fact]
    public async Task HomeMap_LoadsGpsPointsAndPlannedRoutePoints()
    {
        await using var context = Context();
        var dog = new Dog { Name = "Luna", OwnerId = UserId };
        var plan = new PlannedWalk
        {
            OwnerId = UserId, Dog = dog, Title = "Pot", AreaKey = "maribor", AreaName = "Maribor",
            RoutePoints = [
                new PlannedWalkRoutePoint { Order = 1, Latitude = 46, Longitude = 15 },
                new PlannedWalkRoutePoint { Order = 2, Latitude = 46.01, Longitude = 15.01 }
            ]
        };
        var walk = new Walk
        {
            OwnerId = UserId, Dog = dog, PlannedWalk = plan, Status = "Active", StartedAt = DateTime.UtcNow,
            Points = [
                new WalkPoint { Latitude = 46, Longitude = 15, RecordedAt = DateTime.UtcNow.AddMinutes(-1) },
                new WalkPoint { Latitude = 46.005, Longitude = 15.005, RecordedAt = DateTime.UtcNow }
            ]
        };
        var olderWalk = new Walk
        {
            OwnerId = UserId, Dog = dog, Status = "Active", StartedAt = DateTime.UtcNow.AddHours(-1),
            Points = [new WalkPoint { Latitude = 46, Longitude = 15, RecordedAt = DateTime.UtcNow.AddHours(-1) }]
        };
        context.Walks.AddRange(olderWalk, walk);
        await context.SaveChangesAsync();
        var notifications = new NoOpNotifications();
        var calendar = new TestGamificationCalendar();
        var controller = Prepare(new MapController(
            context, new TestEnvironment(), UserManager(context), new NoOpImages(), new NoOpEmail(), notifications,
            new GamificationService(context, notifications, calendar), new DogProgressionService(context), new MapStampService(),
            new GamificationRewardBuilder(), calendar, new UserAchievementService(context, notifications)));

        var result = Assert.IsType<ViewResult>(await controller.Index());
        var activeWalk = Assert.IsType<Walk>(result.ViewData["ActiveWalk"]);
        Assert.Equal(walk.Id, activeWalk.Id);
        Assert.Equal(2, activeWalk.Points?.Count);
        Assert.Equal(2, activeWalk.PlannedWalk?.RoutePoints?.Count);
        Assert.Equal(dog.Id, activeWalk.Dog?.Id);
    }

    [Fact]
    public async Task DogCreation_NotificationFailureRollsBackDogAchievementAndNotification()
    {
        await using var context = Context();
        var notifications = new FailingAchievementNotifications(context);
        var controller = Prepare(new DogsController(context, UserManager(context), new NoOpImages(), new DogProgressionService(context), new UserAchievementService(context, notifications)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Create(new DogCreateViewModel { Name = "Luna", IsFirstDog = true }));

        await using var verification = Context();
        Assert.Empty(await verification.Dogs.ToListAsync());
        Assert.Empty(await verification.UserAchievements.ToListAsync());
        Assert.Empty(await verification.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task BinSubmission_NotificationFailureRollsBackBinAchievementAndProgression()
    {
        await using var context = Context();
        var notifications = new FailingAchievementNotifications(context);
        var calendar = new TestGamificationCalendar();
        var controller = Prepare(new MapController(
            context, new TestEnvironment(), UserManager(context), new NoOpImages(), new NoOpEmail(), notifications,
            new GamificationService(context, notifications, calendar), new DogProgressionService(context), new MapStampService(),
            new GamificationRewardBuilder(), calendar, new UserAchievementService(context, notifications)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Add(new TrashBinViewModel { Name = "Predlog", Latitude = 46.05, Longitude = 14.5 }));

        await using var verification = Context();
        Assert.Empty(await verification.TrashBins.ToListAsync());
        Assert.Empty(await verification.UserAchievements.ToListAsync());
        Assert.Empty(await verification.UserNotifications.ToListAsync());
        Assert.Empty(await verification.UserXpEvents.ToListAsync());
        Assert.Empty(await verification.UserStreaks.ToListAsync());
        Assert.Empty(await verification.UserGamificationProfiles.ToListAsync());
    }

    private T Prepare<T>(T controller) where T : Controller
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], "Test"))
            }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempDataProvider());
        return controller;
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_databasePath};Default Timeout=15;Pooling=False").Options);

    private static UserManager<ApplicationUser> UserManager(ApplicationDbContext context) =>
        new(new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(context), Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(), [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private Dictionary<string, object> _data = [];
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(_data);
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = new Dictionary<string, object>(values);
    }

    private sealed class NoOpNotifications : INotificationService
    {
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null) => Task.CompletedTask;
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) => Task.CompletedTask;
    }

    private sealed class FailingAchievementNotifications : INotificationService
    {
        private readonly ApplicationDbContext _context;

        public FailingAchievementNotifications(ApplicationDbContext context) => _context = context;

        public async Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null)
        {
            if (!type.StartsWith("Achievement:", StringComparison.Ordinal)) return;
            _context.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Body = body,
                LinkUrl = linkUrl,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            throw new InvalidOperationException("Simulated failure after notification persistence.");
        }

        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) =>
            CreateAsync(userId, type, title, body, linkUrl);
    }

    private sealed class NoOpImages : ICloudinaryService
    {
        public Task<string?> UploadImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadTrashBinImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadWalkImageAsync(IFormFile file) => Task.FromResult<string?>(null);
    }

    private sealed class NoOpEmail : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "DoggyDrop.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
