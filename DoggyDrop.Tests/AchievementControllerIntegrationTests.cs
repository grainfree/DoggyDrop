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
