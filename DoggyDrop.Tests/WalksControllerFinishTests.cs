using System.Security.Claims;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalksControllerFinishTests : IDisposable
{
    private const string UserId = "test-user";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"doggydrop-tests-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Finish_CompletesAndAwardsEachRewardOnce()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000);
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Finish(walkId, null, null);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(WalksController.Details), redirect.ActionName);
        Assert.Equal("Completed", (await context.Walks.SingleAsync()).Status);
        Assert.Single(await context.UserXpEvents.ToListAsync());
        Assert.Single(await context.DogXpEvents.ToListAsync());
        Assert.Single(await context.UserStreaks.ToListAsync());
        Assert.True(controller.TempData.ContainsKey($"WalkRewardResult:{walkId}"));
    }

    [Fact]
    public async Task Finish_SequentialRepeatDoesNotAwardAgain()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000);
        await using (var firstContext = CreateContext())
        {
            await CreateController(firstContext).Finish(walkId, null, null);
        }

        await using (var secondContext = CreateContext())
        {
            await CreateController(secondContext).Finish(walkId, null, null);
        }

        await using var verification = CreateContext();
        Assert.Single(await verification.UserXpEvents.ToListAsync());
        Assert.Single(await verification.DogXpEvents.ToListAsync());
        Assert.Single(await verification.UserStreaks.ToListAsync());
    }

    [Fact]
    public async Task Finish_ConcurrentRequestsAwardOnlyOnce()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000);
        var first = FinishWithNewContextAsync(walkId);
        var second = FinishWithNewContextAsync(walkId);

        await Task.WhenAll(first, second);

        await using var verification = CreateContext();
        Assert.Equal("Completed", (await verification.Walks.SingleAsync()).Status);
        Assert.Single(await verification.UserXpEvents.ToListAsync());
        Assert.Single(await verification.DogXpEvents.ToListAsync());
        Assert.Single(await verification.UserStreaks.ToListAsync());
    }

    [Fact]
    public async Task Details_DoesNotConsumeOrDisplayRewardForAnotherWalk()
    {
        var walkA = await SeedAsync(distanceMeters: 2_000);
        int walkB;
        await using (var setup = CreateContext())
        {
            var dogId = await setup.Dogs.Select(dog => dog.Id).SingleAsync();
            var secondWalk = new Walk
            {
                OwnerId = UserId,
                DogId = dogId,
                Status = "Completed",
                StartedAt = DateTime.UtcNow.AddHours(-1),
                EndedAt = DateTime.UtcNow
            };
            setup.Walks.Add(secondWalk);
            await setup.SaveChangesAsync();
            walkB = secondWalk.Id;
        }

        await using var context = CreateContext();
        var controller = CreateController(context);
        controller.TempData[$"WalkRewardResult:{walkA}"] = "{\"WalkId\":" + walkA + "}";

        var result = await controller.Details(walkB);

        Assert.IsType<ViewResult>(result);
        Assert.Null(controller.ViewBag.WalkRewardResult);
        Assert.True(controller.TempData.ContainsKey($"WalkRewardResult:{walkA}"));
    }

    [Fact]
    public async Task Details_WithoutTempDataReturnsNormally()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000, status: "Completed");
        await using var context = CreateContext();
        var controller = CreateController(context);

        var result = await controller.Details(walkId);

        Assert.IsType<ViewResult>(result);
        Assert.Null(controller.ViewBag.WalkRewardResult);
    }

    [Fact]
    public async Task FirstWalk_StoresCelebrationAndSingleFirstWalkReward()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000);
        await using var context = CreateContext();
        var controller = CreateController(context);

        await controller.Finish(walkId, null, null);

        Assert.Equal(true, controller.TempData[$"FirstWalk:Show:{walkId}"]);
        var json = Assert.IsType<string>(controller.TempData[$"WalkRewardResult:{walkId}"]);
        var reward = System.Text.Json.JsonSerializer.Deserialize<GamificationRewardResultViewModel>(json);
        Assert.NotNull(reward);
        Assert.Equal(walkId, reward.WalkId);
        Assert.Single(reward.UnlockedAchievements, achievement => achievement.Name == "Prvi sprehod");

        var visibleAchievements = reward.GetVisibleAchievements(showFirstWalkCelebration: true);
        Assert.DoesNotContain(visibleAchievements, achievement => achievement.Name == "Prvi sprehod");
    }

    private async Task<IActionResult> FinishWithNewContextAsync(int walkId)
    {
        await using var context = CreateContext();
        return await CreateController(context).Finish(walkId, null, null);
    }

    private async Task<int> SeedAsync(double distanceMeters, string status = "Active")
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
        await context.Database.EnsureCreatedAsync();

        var user = new ApplicationUser { Id = UserId, UserName = "tester@example.test", Email = "tester@example.test" };
        var dog = new Dog { Name = "Floyd", OwnerId = UserId };
        context.Users.Add(user);
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();

        var walk = new Walk
        {
            OwnerId = UserId,
            DogId = dog.Id,
            Status = status,
            DistanceMeters = distanceMeters,
            StartedAt = DateTime.UtcNow.AddMinutes(-30),
            EndedAt = status == "Completed" ? DateTime.UtcNow : null
        };
        context.Walks.Add(walk);
        await context.SaveChangesAsync();
        return walk.Id;
    }

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=15;Pooling=False")
            .Options;
        return new ApplicationDbContext(options);
    }

    private WalksController CreateController(ApplicationDbContext context)
    {
        var notifications = new NoOpNotificationService();
        var controller = new WalksController(
            context,
            CreateUserManager(context),
            notifications,
            new NoOpImageService(),
            new GamificationService(context, notifications),
            new DogProgressionService(context),
            new NoOpPlannerService(),
            new GamificationRewardBuilder());

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)],
                "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new DictionaryTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
    {
        var store = new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(context);
        return new UserManager<ApplicationUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [],
            [],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>(_values);
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = new Dictionary<string, object>(values);
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/Walks/Planner";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/Walks/Planner";
        public string? RouteUrl(UrlRouteContext routeContext) => "/Walks/Planner";
    }

    private sealed class NoOpNotificationService : INotificationService
    {
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null) => Task.CompletedTask;
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) => Task.CompletedTask;
    }

    private sealed class NoOpImageService : ICloudinaryService
    {
        public Task<string?> UploadImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadTrashBinImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadWalkImageAsync(IFormFile file) => Task.FromResult<string?>(null);
    }

    private sealed class NoOpPlannerService : IOsmWalkPlannerService
    {
        public Task<PlannedWalkRoute?> PlanAsync(double startLatitude, double startLongitude, double targetDistanceKm, IReadOnlyList<TrashBin> bins, string walkStyle, string dogEnergy, bool includeBins, bool includePark, bool includeWater, bool includeDogFriendly, CancellationToken cancellationToken = default)
            => Task.FromResult<PlannedWalkRoute?>(null);
    }
}
