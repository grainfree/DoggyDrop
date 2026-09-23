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
        Assert.Single(await context.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.WalkFirst).ToListAsync());
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
        Assert.Single(await verification.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.WalkFirst).ToListAsync());
    }

    [Fact]
    public async Task Finish_JsonResponsePreservesRewardUntilDetailsRequest()
    {
        var walkId = await SeedAsync(distanceMeters: 2_000);
        await using var context = CreateContext();
        var controller = CreateController(context);
        controller.Request.Headers.Accept = "application/json";

        var result = await controller.Finish(walkId, null, null);

        var json = Assert.IsType<JsonResult>(result);
        var redirectUrl = json.Value!.GetType().GetProperty("redirectUrl")!.GetValue(json.Value);
        Assert.Equal($"/Walks/Details/{walkId}", redirectUrl);
        Assert.True(controller.TempData.ContainsKey($"WalkRewardResult:{walkId}"));
        Assert.Equal("Completed", (await context.Walks.SingleAsync()).Status);
    }

    [Fact]
    public async Task AddPoint_ReportsPersistedCountAndRecordedDistance()
    {
        var walkId = await SeedAsync(distanceMeters: 0);
        await using var context = CreateContext();
        var controller = CreateController(context);

        await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15 });
        var result = Assert.IsType<JsonResult>(await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46.001, Longitude = 15 }));

        Assert.Equal(2, result.Value!.GetType().GetProperty("pointCount")!.GetValue(result.Value));
        Assert.Equal(2, await context.WalkPoints.CountAsync());
        Assert.True((await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters > 100);
    }

    [Fact]
    public async Task AddPoint_RejectsNonFiniteCoordinates()
    {
        var walkId = await SeedAsync(distanceMeters: 0);
        await using var context = CreateContext();

        var result = await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = double.NaN, Longitude = 15 });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(await context.WalkPoints.AnyAsync());
    }

    [Fact]
    public async Task AddPoint_SequentialDuplicateIsNotPersisted()
    {
        var walkId = await SeedAsync(0);
        var time = DateTime.UtcNow.AddSeconds(-10);
        await using var context = CreateContext();
        var controller = CreateController(context);
        await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });
        var duplicate = Assert.IsType<JsonResult>(await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time }));

        Assert.Equal("duplicate", ResultValue(duplicate, "outcome"));
        Assert.Equal(1, ResultValue(duplicate, "pointCount"));
        Assert.Equal(1, await context.WalkPoints.CountAsync());
        Assert.Equal(0, (await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
    }

    [Fact]
    public async Task AddPoint_OutOfOrderDoesNotChangeDistance()
    {
        var walkId = await SeedAsync(0);
        var time = DateTime.UtcNow.AddSeconds(-10);
        await using var context = CreateContext();
        var controller = CreateController(context);
        await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });
        var older = Assert.IsType<JsonResult>(await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46.001, Longitude = 15, RecordedAt = time.AddSeconds(-1) }));

        Assert.Equal("out-of-order", ResultValue(older, "outcome"));
        Assert.Equal(1, await context.WalkPoints.CountAsync());
        Assert.Equal(0, (await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
    }

    [Fact]
    public async Task AddPoint_ConcurrentDuplicatesPersistOnce()
    {
        var walkId = await SeedAsync(0);
        var time = DateTime.UtcNow.AddSeconds(-10);
        async Task<IActionResult> SubmitAsync()
        {
            await using var context = CreateContext();
            return await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });
        }

        var results = await Task.WhenAll(SubmitAsync(), SubmitAsync());
        Assert.Equal(1, results.Count(result => ResultValue(Assert.IsType<JsonResult>(result), "outcome")?.ToString() == "accepted"));
        await using var verification = CreateContext();
        Assert.Single(await verification.WalkPoints.ToListAsync());
        Assert.Equal(0, (await verification.Walks.SingleAsync()).DistanceMeters);
    }

    [Fact]
    public async Task AddPoint_ConcurrentSamplesUseLatestAcceptedPoint()
    {
        var walkId = await SeedAsync(0);
        var time = DateTime.UtcNow.AddSeconds(-20);
        await using (var setup = CreateContext())
            await CreateController(setup).AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });

        async Task SubmitAsync(double latitude, int seconds)
        {
            await using var context = CreateContext();
            await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = latitude, Longitude = 15, RecordedAt = time.AddSeconds(seconds) });
        }
        await Task.WhenAll(SubmitAsync(46.001, 5), SubmitAsync(46.002, 10));

        await using var verification = CreateContext();
        var points = await verification.WalkPoints.OrderBy(point => point.RecordedAt).ToListAsync();
        Assert.InRange(points.Count, 2, 3);
        var expected = points.Zip(points.Skip(1), (first, second) =>
            6371000d * 2 * Math.Asin(Math.Sqrt(Math.Pow(Math.Sin((second.Latitude - first.Latitude) * Math.PI / 360), 2)))).Sum();
        Assert.InRange((await verification.Walks.SingleAsync()).DistanceMeters, expected - 0.1, expected + 0.1);
    }

    [Fact]
    public async Task AddPoint_RacingFinishNeverWritesAfterCompletion()
    {
        var walkId = await SeedAsync(2_000);
        var time = DateTime.UtcNow.AddSeconds(-10);
        async Task<IActionResult> SubmitAsync()
        {
            await using var context = CreateContext();
            return await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });
        }

        var results = await Task.WhenAll(SubmitAsync(), FinishWithNewContextAsync(walkId));
        await using var verification = CreateContext();
        var completed = await verification.Walks.SingleAsync();
        Assert.Equal("Completed", completed.Status);
        var count = await verification.WalkPoints.CountAsync();
        Assert.InRange(count, 0, 1);
        Assert.Single(await verification.UserXpEvents.ToListAsync());
        Assert.Single(await verification.DogXpEvents.ToListAsync());
        Assert.IsType<ConflictObjectResult>(await SubmitAsync());
        Assert.Equal(count, await verification.WalkPoints.CountAsync());
        Assert.Equal(completed.DistanceMeters, (await verification.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Interrupted")]
    public async Task AddPoint_AfterTerminalStatusDoesNotPersist(string status)
    {
        var walkId = await SeedAsync(250, status);
        await using var context = CreateContext();
        var result = await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15 });

        Assert.Equal("no-longer-active", ResultValue(Assert.IsType<ConflictObjectResult>(result), "outcome"));
        Assert.Empty(await context.WalkPoints.ToListAsync());
        Assert.Equal(250, (await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
    }

    [Fact]
    public async Task AddPoint_StaleWalkInterruptsWithoutPointOrRewards()
    {
        var walkId = await SeedAsync(250);
        await using (var setup = CreateContext())
        {
            var walk = await setup.Walks.SingleAsync();
            walk.StartedAt = DateTime.UtcNow.AddDays(-2);
            await setup.SaveChangesAsync();
        }
        await using var context = CreateContext();
        var result = await CreateController(context).AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15 });

        Assert.Equal("Interrupted", ResultValue(Assert.IsType<ConflictObjectResult>(result), "status"));
        Assert.Empty(await context.WalkPoints.ToListAsync());
        Assert.Equal(250, (await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
        Assert.False(await context.UserXpEvents.AnyAsync());
    }

    [Fact]
    public async Task AddPoint_RejectsClearlyUnusableAccuracyAndTeleport()
    {
        var walkId = await SeedAsync(0);
        var time = DateTime.UtcNow.AddSeconds(-10);
        await using var context = CreateContext();
        var controller = CreateController(context);
        var badAccuracy = await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, AccuracyMeters = 1500 });
        Assert.Equal("invalid", ResultValue(Assert.IsType<BadRequestObjectResult>(badAccuracy), "outcome"));
        await controller.AddPoint(walkId, new WalkPointInput { Latitude = 46, Longitude = 15, RecordedAt = time });
        var teleport = await controller.AddPoint(walkId, new WalkPointInput { Latitude = 47, Longitude = 15, RecordedAt = time.AddSeconds(1), AccuracyMeters = 15 });

        Assert.Equal("invalid", ResultValue(Assert.IsType<BadRequestObjectResult>(teleport), "outcome"));
        Assert.Single(await context.WalkPoints.ToListAsync());
        Assert.Equal(0, (await context.Walks.AsNoTracking().SingleAsync()).DistanceMeters);
    }

    private static object? ResultValue(IActionResult result, string name)
    {
        var value = result switch { JsonResult json => json.Value, ObjectResult other => other.Value, _ => null };
        return value?.GetType().GetProperty(name)?.GetValue(value);
    }

    [Fact]
    public void WalkFormatting_UsesSlovenianPrecisionAndDuration()
    {
        Assert.Equal("0,02 km", SlovenianFormatting.WalkDistance(20));
        Assert.Equal("22 min", SlovenianFormatting.WalkDuration(TimeSpan.FromMinutes(22)));
        Assert.Equal("1 h 5 min", SlovenianFormatting.WalkDuration(TimeSpan.FromMinutes(65)));
    }

    [Theory]
    [InlineData(0, "0 m")]
    [InlineData(4, "4 m")]
    [InlineData(9, "9 m")]
    [InlineData(10, "0,01 km")]
    [InlineData(20, "0,02 km")]
    [InlineData(100, "0,10 km")]
    [InlineData(1000, "1,00 km")]
    public void WalkDistance_FormatsShortAndLongDistances(double meters, string expected)
    {
        Assert.Equal(expected, SlovenianFormatting.WalkDistance(meters));
    }

    [Fact]
    public async Task Finish_StaleWalkIsInterruptedAtLastGpsPointWithoutRewards()
    {
        var walkId = await SeedAsync(distanceMeters: 850);
        DateTime lastActivity;
        await using (var setup = CreateContext())
        {
            var walk = await setup.Walks.SingleAsync();
            walk.StartedAt = DateTime.UtcNow.AddDays(-3);
            lastActivity = DateTime.UtcNow.AddDays(-2);
            setup.WalkPoints.Add(new WalkPoint { WalkId = walkId, RecordedAt = lastActivity, Latitude = 46, Longitude = 15 });
            await setup.SaveChangesAsync();
        }

        await using var context = CreateContext();
        await CreateController(context).Finish(walkId, null, null);

        var recovered = await context.Walks.AsNoTracking().SingleAsync();
        Assert.Equal("Interrupted", recovered.Status);
        Assert.Equal(lastActivity, recovered.EndedAt);
        Assert.Equal(850, recovered.DistanceMeters);
        Assert.False(await context.UserXpEvents.AnyAsync());
        Assert.False(await context.DogXpEvents.AnyAsync());
        Assert.False(await context.UserAchievements.AnyAsync());
        Assert.False(await context.UserStreaks.AnyAsync());
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
        Assert.Single(await verification.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.WalkFirst).ToListAsync());
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
        Assert.Single(reward.UnlockedAchievements, achievement => achievement.Key == UserAchievementCatalog.WalkFirst);

        var visibleAchievements = reward.GetVisibleAchievements(showFirstWalkCelebration: true);
        Assert.DoesNotContain(visibleAchievements, achievement => achievement.Key == UserAchievementCatalog.WalkFirst);
    }

    [Theory]
    [InlineData(10_000, UserAchievementCatalog.Walk10Km, "Mestni pohodnik")]
    [InlineData(100_000, UserAchievementCatalog.Walk100Km, "Mojster poti")]
    public async Task Finish_UnlocksDurableDistanceAchievementAtThreshold(double distanceMeters, string key, string displayName)
    {
        var walkId = await SeedAsync(distanceMeters);
        await using var context = CreateContext();
        var controller = CreateController(context);

        await controller.Finish(walkId, null, null);

        Assert.True(await context.UserAchievements.AnyAsync(item => item.AchievementKey == key));
        var json = Assert.IsType<string>(controller.TempData[$"WalkRewardResult:{walkId}"]);
        var reward = System.Text.Json.JsonSerializer.Deserialize<GamificationRewardResultViewModel>(json)!;
        Assert.Contains(reward.UnlockedAchievements, item => item.Name == displayName);
    }

    [Theory]
    [InlineData(9_990, 10, UserAchievementCatalog.Walk10Km)]
    [InlineData(99_990, 10, UserAchievementCatalog.Walk100Km)]
    public async Task Finish_UnlocksDistanceAchievementExactlyAtCumulativeBoundary(double priorMeters, double finishingMeters, string key)
    {
        var walkId = await SeedAsync(finishingMeters);
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(item => item.Id).SingleAsync();
        context.Walks.Add(new Walk { OwnerId = UserId, DogId = dogId, Status = "Completed", DistanceMeters = priorMeters, StartedAt = DateTime.UtcNow.AddHours(-2), EndedAt = DateTime.UtcNow.AddHours(-1) });
        await context.SaveChangesAsync();

        await CreateController(context).Finish(walkId, null, null);

        Assert.Single(await context.UserAchievements.Where(item => item.AchievementKey == key).ToListAsync());
    }

    [Fact]
    public async Task Finish_DoesNotSuggestOwnedTenKilometerGoalWhenCorrectedProgressIsLower()
    {
        var walkId = await SeedAsync(1_000);
        await using var context = CreateContext();
        context.UserAchievements.Add(new UserAchievement { UserId = UserId, AchievementKey = UserAchievementCatalog.Walk10Km, UnlockedAt = DateTime.UtcNow.AddDays(-1), CreatedAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
        var controller = CreateController(context);

        await controller.Finish(walkId, null, null);

        var json = Assert.IsType<string>(controller.TempData[$"WalkRewardResult:{walkId}"]);
        var reward = System.Text.Json.JsonSerializer.Deserialize<GamificationRewardResultViewModel>(json)!;
        Assert.Contains("100 km", reward.NextGoal!.Title);
    }

    [Fact]
    public async Task SavingPlannedRoute_DoesNotMaintainExplorerStreak()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(dog => dog.Id).SingleAsync();
        await CreateController(context).SavePlan(dogId, "maribor", 3, "balanced", "auto", null, null);
        Assert.False(await context.UserStreaks.AnyAsync(streak => streak.StreakType == GamificationStreakConstants.Explorer));
    }

    [Fact]
    public async Task StartingPlannedRoute_DoesNotMaintainExplorerStreak()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(dog => dog.Id).SingleAsync();
        await CreateController(context).StartPlanned(dogId, "maribor", 3, "balanced", "auto", null, null);
        Assert.False(await context.UserStreaks.AnyAsync(streak => streak.StreakType == GamificationStreakConstants.Explorer));
    }

    [Fact]
    public async Task Start_WithSavedPlan_CompletesStartStopOnly()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(dog => dog.Id).SingleAsync();
        var plan = new PlannedWalk
        {
            OwnerId = UserId,
            DogId = dogId,
            Title = "Testna pot",
            AreaKey = "maribor",
            AreaName = "Maribor",
            Stops = new List<PlannedWalkStop>
            {
                new() { Order = 1, Name = "Start", Type = "start", Latitude = 46, Longitude = 15 },
                new() { Order = 2, Name = "Koš", Type = "bin", Latitude = 46.01, Longitude = 15.01 }
            }
        };
        context.PlannedWalks.Add(plan);
        await context.SaveChangesAsync();

        await CreateController(context).Start(dogId, plan.Id);

        var active = await context.Walks.SingleAsync(walk => walk.Status == "Active");
        var completedTypes = await context.WalkStopCompletions
            .Where(completion => completion.WalkId == active.Id)
            .Select(completion => completion.PlannedWalkStop!.Type)
            .ToListAsync();
        Assert.Equal(["start"], completedTypes);
    }

    [Fact]
    public async Task Active_PreservesOrderedActionableStopsWithoutRouteGeometry()
    {
        await SeedAsync(0, status: "Completed");
        int dogId;
        int planId;
        await using (var setup = CreateContext())
        {
            dogId = await setup.Dogs.Select(dog => dog.Id).SingleAsync();
            var plan = new PlannedWalk
            {
                OwnerId = UserId, DogId = dogId, Title = "Pot brez geometrije", AreaKey = "maribor", AreaName = "Maribor",
                Stops = [
                    new PlannedWalkStop { Order = 4, Name = "Cilj", Type = "finish", Latitude = 46.03, Longitude = 15.03 },
                    new PlannedWalkStop { Order = 2, Name = "Prvi koš", Type = "bin", Latitude = 46.01, Longitude = 15.01 },
                    new PlannedWalkStop { Order = 1, Name = "Start", Type = "start", Latitude = 46, Longitude = 15 },
                    new PlannedWalkStop { Order = 3, Name = "Park", Type = "park", Latitude = 46.02, Longitude = 15.02 }
                ]
            };
            setup.PlannedWalks.Add(plan);
            await setup.SaveChangesAsync();
            planId = plan.Id;
        }

        int walkId;
        await using (var startContext = CreateContext())
        {
            await CreateController(startContext).Start(dogId, planId);
            walkId = await startContext.Walks.Where(item => item.Status == "Active").Select(item => item.Id).SingleAsync();
        }

        await using var activeContext = CreateContext();
        var result = Assert.IsType<ViewResult>(await CreateController(activeContext).Active(walkId));
        var walk = Assert.IsType<Walk>(result.Model);
        Assert.Empty(walk.PlannedWalk?.RoutePoints ?? []);
        Assert.Equal(["Prvi koš", "Park"], walk.PlannedWalk!.Stops!
            .OrderBy(stop => stop.Order)
            .Where(stop => stop.Type is not ("start" or "finish"))
            .Select(stop => stop.Name));
        Assert.Equal("start", Assert.Single(walk.StopCompletions!).PlannedWalkStop?.Type);
    }

    [Fact]
    public async Task Start_FreeWalk_CreatesActiveWalkWithoutPlanOrStops()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(dog => dog.Id).SingleAsync();

        var result = await CreateController(context).Start(dogId);

        var walk = await context.Walks.SingleAsync(item => item.Status == "Active");
        Assert.Null(walk.PlannedWalkId);
        Assert.Equal(dogId, walk.DogId);
        Assert.False(await context.WalkStopCompletions.AnyAsync(item => item.WalkId == walk.Id));
        Assert.IsType<RedirectToActionResult>(result);
    }

    [Fact]
    public async Task Start_RejectsDogNotOwnedByCurrentUser()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();

        var result = await CreateController(context).Start(int.MaxValue);

        Assert.IsType<NotFoundResult>(result);
        Assert.False(await context.Walks.AnyAsync(item => item.Status == "Active"));
    }

    [Fact]
    public async Task Start_WithoutOwnedDogsReturnsNotFound()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        context.Users.Add(new ApplicationUser { Id = UserId, UserName = "tester@example.test" });
        await context.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await CreateController(context).Start(1));
        Assert.Empty(await context.Walks.ToListAsync());
    }

    [Fact]
    public async Task Start_WithMultipleDogsUsesSelectedOwnedDog()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var selected = new Dog { Name = "Luna", OwnerId = UserId };
        context.Dogs.Add(selected);
        await context.SaveChangesAsync();

        await CreateController(context).Start(selected.Id);

        Assert.Equal(selected.Id, (await context.Walks.SingleAsync(item => item.Status == "Active")).DogId);
    }

    [Fact]
    public async Task Start_RejectsExistingOtherUsersDog()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var otherUser = new ApplicationUser { Id = "other-user", UserName = "other@example.test" };
        var otherDog = new Dog { Name = "Tuji pes", OwnerId = otherUser.Id };
        context.Users.Add(otherUser);
        context.Dogs.Add(otherDog);
        await context.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await CreateController(context).Start(otherDog.Id));
        Assert.False(await context.Walks.AnyAsync(item => item.Status == "Active"));
    }

    [Fact]
    public async Task Start_WhenAlreadyActiveDoesNotCreateAnotherWalk()
    {
        var existingId = await SeedAsync(0);
        await using var context = CreateContext();
        var dogId = await context.Dogs.Select(dog => dog.Id).SingleAsync();

        var result = Assert.IsType<RedirectToActionResult>(await CreateController(context).Start(dogId));

        Assert.Equal(nameof(WalksController.Index), result.ActionName);
        Assert.Equal([existingId], await context.Walks.Where(item => item.Status == "Active").Select(item => item.Id).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Start_ConcurrentRequestsCreateOneActiveWalk(bool withPlan)
    {
        await SeedAsync(0, status: "Completed");
        int dogId;
        int? planId = null;
        await using (var setup = CreateContext())
        {
            dogId = await setup.Dogs.Select(dog => dog.Id).SingleAsync();
            if (withPlan)
            {
                var plan = new PlannedWalk
                {
                    OwnerId = UserId, DogId = dogId, Title = "Testna pot", AreaKey = "maribor", AreaName = "Maribor",
                    Stops = [
                        new PlannedWalkStop { Order = 1, Name = "Start", Type = "start", Latitude = 46, Longitude = 15 },
                        new PlannedWalkStop { Order = 2, Name = "Koš", Type = "bin", Latitude = 46.01, Longitude = 15.01 }
                    ]
                };
                setup.PlannedWalks.Add(plan);
                await setup.SaveChangesAsync();
                planId = plan.Id;
            }
        }

        async Task<IActionResult> RequestAsync()
        {
            await using var context = CreateContext();
            await Task.Yield();
            return await CreateController(context).Start(dogId, planId);
        }

        var results = await Task.WhenAll(RequestAsync(), RequestAsync());
        Assert.Equal(1, results.Count(result => result is RedirectToActionResult redirect && redirect.ActionName == nameof(WalksController.Active)));
        Assert.Equal(1, results.Count(result => result is RedirectToActionResult redirect && redirect.ActionName == nameof(WalksController.Index)));
        await using var verification = CreateContext();
        var active = Assert.Single(await verification.Walks.Where(item => item.Status == "Active").ToListAsync());
        Assert.Equal(planId, active.PlannedWalkId);
        Assert.Equal(withPlan ? 1 : 0, await verification.WalkStopCompletions.CountAsync(item => item.WalkId == active.Id));
    }

    [Fact]
    public async Task Start_DifferentUsersCanEachCreateActiveWalk()
    {
        await SeedAsync(0, status: "Completed");
        int firstDogId;
        int secondDogId;
        await using (var setup = CreateContext())
        {
            firstDogId = await setup.Dogs.Where(dog => dog.OwnerId == UserId).Select(dog => dog.Id).SingleAsync();
            var secondUser = new ApplicationUser { Id = "second-user", UserName = "second@example.test" };
            var secondDog = new Dog { Name = "Rex", OwnerId = secondUser.Id };
            setup.Users.Add(secondUser);
            setup.Dogs.Add(secondDog);
            await setup.SaveChangesAsync();
            secondDogId = secondDog.Id;
        }

        async Task<IActionResult> RequestAsync(int dogId, string ownerId)
        {
            await using var context = CreateContext();
            await Task.Yield();
            return await CreateController(context, userId: ownerId).Start(dogId);
        }

        var results = await Task.WhenAll(RequestAsync(firstDogId, UserId), RequestAsync(secondDogId, "second-user"));
        Assert.All(results, result => Assert.Equal(nameof(WalksController.Active), Assert.IsType<RedirectToActionResult>(result).ActionName));
        await using var verification = CreateContext();
        Assert.Equal(2, await verification.Walks.CountAsync(item => item.Status == "Active"));
    }

    [Fact]
    public async Task StartPlanned_ConcurrentRequestsCreateOneWalkAndPlan()
    {
        await SeedAsync(0, status: "Completed");
        int dogId;
        await using (var setup = CreateContext())
            dogId = await setup.Dogs.Select(dog => dog.Id).SingleAsync();

        async Task<IActionResult> RequestAsync()
        {
            await using var context = CreateContext();
            await Task.Yield();
            return await CreateController(context).StartPlanned(dogId, "maribor", 3, "balanced", "auto", null, null);
        }

        var results = await Task.WhenAll(RequestAsync(), RequestAsync());
        Assert.All(results, result => Assert.Equal(nameof(WalksController.Active), Assert.IsType<RedirectToActionResult>(result).ActionName));
        await using var verification = CreateContext();
        Assert.Single(await verification.Walks.Where(item => item.Status == "Active").ToListAsync());
        Assert.Single(await verification.PlannedWalks.ToListAsync());
    }

    [Fact]
    public async Task AddPhoto_JsonUploadFailure_DoesNotEndActiveWalk()
    {
        var walkId = await SeedAsync(0);
        await using var context = CreateContext();
        var controller = CreateController(context);
        controller.HttpContext.Request.Headers.Accept = "application/json";
        var photo = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "walk.jpg");

        var result = await controller.AddPhoto(walkId, photo, null);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Active", (await context.Walks.SingleAsync(item => item.Id == walkId)).Status);
        Assert.False(await context.WalkPhotos.AnyAsync(item => item.WalkId == walkId));
    }

    [Fact]
    public async Task AddPhoto_JsonUploadSuccess_KeepsWalkActive()
    {
        var walkId = await SeedAsync(0);
        await using var context = CreateContext();
        var controller = CreateController(context, imageService: new NoOpImageService("https://example.test/walk.jpg"));
        controller.HttpContext.Request.Headers.Accept = "application/json";
        var photo = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "walk.jpg");

        var result = await controller.AddPhoto(walkId, photo, null);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("Active", (await context.Walks.SingleAsync(item => item.Id == walkId)).Status);
        Assert.Single(await context.WalkPhotos.Where(item => item.WalkId == walkId).ToListAsync());
    }

    [Theory]
    [InlineData("Interrupted", false)]
    [InlineData("Completed", true)]
    public async Task AddPhoto_RespectsExistingWalkStatusRules(string status, bool allowed)
    {
        var walkId = await SeedAsync(0, status);
        await using var context = CreateContext();
        var controller = CreateController(context, imageService: new NoOpImageService("https://example.test/walk.jpg"));
        controller.HttpContext.Request.Headers.Accept = "application/json";
        var photo = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "walk.jpg");

        var result = await controller.AddPhoto(walkId, photo, null);

        if (allowed) Assert.IsType<OkObjectResult>(result);
        else Assert.IsType<NotFoundResult>(result);
        Assert.Equal(allowed ? 1 : 0, await context.WalkPhotos.CountAsync(item => item.WalkId == walkId));
        Assert.Equal(status, (await context.Walks.SingleAsync(item => item.Id == walkId)).Status);
    }

    [Fact]
    public async Task AddPhoto_RejectsAnotherUsersWalkAndInvalidId()
    {
        await SeedAsync(0, status: "Completed");
        await using var context = CreateContext();
        var otherUser = new ApplicationUser { Id = "other-user", UserName = "other@example.test" };
        var otherDog = new Dog { Name = "Tuji pes", OwnerId = otherUser.Id };
        context.Users.Add(otherUser);
        context.Dogs.Add(otherDog);
        await context.SaveChangesAsync();
        var otherWalk = new Walk { OwnerId = otherUser.Id, DogId = otherDog.Id, Status = "Active" };
        context.Walks.Add(otherWalk);
        await context.SaveChangesAsync();
        var controller = CreateController(context, imageService: new NoOpImageService("https://example.test/walk.jpg"));
        var photo = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "walk.jpg");

        Assert.IsType<NotFoundResult>(await controller.AddPhoto(otherWalk.Id, photo, null));
        Assert.IsType<NotFoundResult>(await controller.AddPhoto(int.MaxValue, photo, null));
        Assert.False(await context.WalkPhotos.AnyAsync());
    }

    [Fact]
    public async Task FinishAcrossLjubljanaMidnight_UsesCapturedFinishDateOnce()
    {
        var walkId = await SeedAsync(1_000);
        await using var context = CreateContext();
        var walk = await context.Walks.SingleAsync();
        walk.StartedAt = new DateTime(2026, 7, 10, 21, 50, 0, DateTimeKind.Utc);
        await context.SaveChangesAsync();
        var calendar = new TestGamificationCalendar(new DateTime(2026, 7, 10, 22, 10, 0, DateTimeKind.Utc));
        var controller = CreateController(context, calendar);

        await controller.Finish(walkId, null, null);
        await controller.Finish(walkId, null, null);

        var streak = await context.UserStreaks.SingleAsync(item => item.StreakType == GamificationStreakConstants.Walk);
        Assert.Equal(new DateOnly(2026, 7, 11), streak.LastActivityDate);
        Assert.Equal(1, streak.CurrentDays);
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

    private WalksController CreateController(ApplicationDbContext context, TestGamificationCalendar? calendar = null, ICloudinaryService? imageService = null, string userId = UserId)
    {
        var notifications = new NoOpNotificationService();
        calendar ??= new TestGamificationCalendar();
        var controller = new WalksController(
            context,
            CreateUserManager(context),
            notifications,
            imageService ?? new NoOpImageService(),
            new GamificationService(context, notifications, calendar),
            new DogProgressionService(context),
            new NoOpPlannerService(),
            new GamificationRewardBuilder(),
            calendar,
            new UserAchievementService(context, notifications));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
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
        public string? Action(UrlActionContext actionContext) => $"/Walks/{actionContext.Action}/{actionContext.Values?.GetType().GetProperty("id")?.GetValue(actionContext.Values)}";
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
        private readonly string? _walkImageUrl;
        public NoOpImageService(string? walkImageUrl = null) => _walkImageUrl = walkImageUrl;
        public Task<string?> UploadImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadTrashBinImageAsync(IFormFile file) => Task.FromResult<string?>(null);
        public Task<string?> UploadWalkImageAsync(IFormFile file) => Task.FromResult(_walkImageUrl);
    }

    private sealed class NoOpPlannerService : IOsmWalkPlannerService
    {
        public Task<PlannedWalkRoute?> PlanAsync(double startLatitude, double startLongitude, double targetDistanceKm, IReadOnlyList<TrashBin> bins, string walkStyle, string dogEnergy, bool includeBins, bool includePark, bool includeWater, bool includeDogFriendly, CancellationToken cancellationToken = default)
            => Task.FromResult<PlannedWalkRoute?>(null);
    }
}
