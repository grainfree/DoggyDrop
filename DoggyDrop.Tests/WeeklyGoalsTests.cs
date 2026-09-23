using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using DoggyDrop.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WeeklyGoalsTests : IDisposable
{
    private const string Owner = "owner";
    private const string Other = "other";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"doggydrop-weekly-{Guid.NewGuid():N}.db");
    private readonly TestGamificationCalendar _clock = new(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData("2026-09-20T21:59:59Z", "2026-09-14", "2026-09-13T22:00:00Z", "2026-09-20T22:00:00Z")]
    [InlineData("2026-09-20T22:00:00Z", "2026-09-21", "2026-09-20T22:00:00Z", "2026-09-27T22:00:00Z")]
    [InlineData("2026-03-29T21:59:59Z", "2026-03-23", "2026-03-22T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("2026-03-29T22:00:00Z", "2026-03-30", "2026-03-29T22:00:00Z", "2026-04-05T22:00:00Z")]
    [InlineData("2026-10-25T22:59:59Z", "2026-10-19", "2026-10-18T22:00:00Z", "2026-10-25T23:00:00Z")]
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26", "2026-10-25T23:00:00Z", "2026-11-01T23:00:00Z")]
    public void Week_UsesLjubljanaMondayAndExclusiveNextMonday(string instant, string monday, string start, string end)
    {
        var week = WeeklyGoalWeek.At(DateTime.Parse(instant).ToUniversalTime());
        Assert.Equal(DateOnly.Parse(monday), week.Monday);
        Assert.Equal(DateTime.Parse(start).ToUniversalTime(), week.StartUtc);
        Assert.Equal(DateTime.Parse(end).ToUniversalTime(), week.EndUtc);
    }

    [Fact]
    public void DogActivity_RollingSevenDaysDoesNotResetAtLjubljanaMonday()
    {
        var monday = new DateTime(2026, 9, 20, 22, 0, 0, DateTimeKind.Utc);
        var walks = new List<Walk>
        {
            new() { StartedAt = monday.AddMinutes(-30), DistanceMeters = 1_200, Status = "Completed" },
            new() { StartedAt = monday.AddMinutes(30), DistanceMeters = 300, Status = "Completed" },
            new() { StartedAt = monday.AddDays(-7), DistanceMeters = 5_000, Status = "Completed" }
        };

        var activity = ActivityInsightsBuilder.Build(walks, nowUtc: monday.AddHours(1));
        var canonicalWeek = WeeklyGoalWeek.At(monday.AddHours(1));

        Assert.Equal(1.5, activity.WeeklyDistanceKm, 3);
        Assert.Equal("1,5 / 10 km", activity.Challenges.Single(item => item.Title == "10 km v 7 dneh").ProgressText);
        Assert.Equal("6,5 / 10 km", activity.NextMilestoneProgress);
        Assert.True(walks[0].StartedAt < canonicalWeek.StartUtc);
        Assert.True(walks[0].StartedAt >= ActivityInsightsBuilder.RollingSevenDayStartUtc(monday.AddHours(1)));
        Assert.True(walks[2].StartedAt < ActivityInsightsBuilder.RollingSevenDayStartUtc(monday.AddHours(1)));
    }

    [Fact]
    public async Task Progress_UsesOnlyOwnedCompletedWalksActualDistanceAndCurrentWeekPhotos()
    {
        await using var db = await CreateAsync();
        var monday = new DateTime(2026, 9, 20, 22, 0, 0, DateTimeKind.Utc);
        var current = AddWalk(db, Owner, "Completed", monday, 1_200, 90_000);
        AddWalk(db, Owner, "Completed", monday.AddDays(1), 300);
        var prior = AddWalk(db, Owner, "Completed", monday.AddTicks(-1), 4_000);
        AddWalk(db, Owner, "Active", monday.AddDays(2), 6_000);
        AddWalk(db, Owner, "Interrupted", monday.AddDays(2), 6_000);
        AddWalk(db, Owner, "Completed", monday.AddDays(2), 6_000).EndedAt = null;
        AddWalk(db, Owner, "Completed", monday.AddDays(4), 6_000); // Future completion.
        AddWalk(db, Other, "Completed", monday.AddDays(1), 6_000);
        await db.SaveChangesAsync();
        db.WalkPhotos.AddRange(
            new WalkPhoto { WalkId = prior.Id, UserId = Owner, ImageUrl = "old", CreatedAt = monday.AddDays(2) },
            new WalkPhoto { WalkId = current.Id, UserId = Other, ImageUrl = "someone-else", CreatedAt = monday.AddDays(2) });
        await db.SaveChangesAsync();

        var result = await new WeeklyGoalsService(db, _clock).GetForUserAsync(Owner);

        Assert.Equal(2, result.Goals.Single(goal => goal.Key == "walks").Current);
        Assert.Equal(1_500, result.Goals.Single(goal => goal.Key == "distance").Current);
        Assert.Equal("1,5 / 5 km", result.Goals.Single(goal => goal.Key == "distance").ProgressLabel);
        Assert.Equal(0, result.Goals.Single(goal => goal.Key == "photo").Current);
        Assert.Equal("walks", result.NextGoal?.Key);

        db.WalkPhotos.Add(new WalkPhoto { WalkId = current.Id, UserId = Owner, ImageUrl = "owned", CreatedAt = monday.AddDays(2) });
        await db.SaveChangesAsync();
        result = await new WeeklyGoalsService(db, _clock).GetForUserAsync(Owner);
        Assert.Equal(1, result.Goals.Single(goal => goal.Key == "photo").Current);
    }

    [Fact]
    public async Task MondayRollover_ResetsDerivedProgressWithoutChangingWalk()
    {
        await using var db = await CreateAsync();
        var walk = AddWalk(db, Owner, "Completed", new DateTime(2026, 9, 27, 21, 59, 59, DateTimeKind.Utc), 5_000);
        await db.SaveChangesAsync();
        _clock.SetUtcNow(new DateTime(2026, 9, 27, 21, 59, 59, DateTimeKind.Utc));
        var service = new WeeklyGoalsService(db, _clock);
        var sunday = await service.GetForUserAsync(Owner);
        Assert.Equal(1, sunday.Goals.Single(goal => goal.Key == "walks").Current);
        Assert.True(sunday.Goals.Single(goal => goal.Key == "distance").IsComplete);

        _clock.SetUtcNow(new DateTime(2026, 9, 27, 22, 0, 0, DateTimeKind.Utc));
        var monday = await service.GetForUserAsync(Owner);
        Assert.All(monday.Goals, goal => Assert.Equal(0, goal.Current));
        Assert.False(monday.HasActivity);
        Assert.Equal("Completed", walk.Status);
    }

    [Theory]
    [InlineData(0, "0,0 / 5 km", "Še 5,0 km do tedenskega cilja.", 0, false)]
    [InlineData(4, "4 m / 5 km", "Še 5,0 km do tedenskega cilja.", 0, false)]
    [InlineData(4990, "4,99 / 5 km", "Še 10 m do tedenskega cilja.", 99, false)]
    [InlineData(4999, "4,99 / 5 km", "Še 1 m do tedenskega cilja.", 99, false)]
    [InlineData(5000, "5,0 / 5 km", "Še 0,0 km do tedenskega cilja.", 100, true)]
    [InlineData(5001, "5,0 / 5 km", "Še 0,0 km do tedenskega cilja.", 100, true)]
    [InlineData(6200, "6,2 / 5 km", "Še 0,0 km do tedenskega cilja.", 100, true)]
    public async Task Distance_NearTargetTextAndVisualStateStayConsistent(double meters, string progress, string remaining, int percent, bool complete)
    {
        await using var db = await CreateAsync();
        AddWalk(db, Owner, "Completed", new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc), meters);
        await db.SaveChangesAsync();

        var result = await new WeeklyGoalsService(db, _clock).GetForUserAsync(Owner);
        var goal = result.Goals.Single(item => item.Key == "distance");
        Assert.Equal(progress, goal.ProgressLabel);
        Assert.Equal(remaining, goal.RemainingLabel);
        Assert.Equal(percent, goal.ProgressPercent);
        Assert.Equal(complete, goal.IsComplete);
    }

    [Fact]
    public async Task Distance_IgnoresNegativeAndNonFiniteLegacyRows()
    {
        await using var db = await CreateAsync();
        var end = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);
        AddWalk(db, Owner, "Completed", end, 1_500);
        AddWalk(db, Owner, "Completed", end, -500);
        AddWalk(db, Owner, "Completed", end, double.PositiveInfinity);
        await db.SaveChangesAsync();

        var goal = (await new WeeklyGoalsService(db, _clock).GetForUserAsync(Owner)).Goals.Single(item => item.Key == "distance");
        Assert.Equal(1_500, goal.Current);
        Assert.Equal("1,5 / 5 km", goal.ProgressLabel);
        Assert.InRange(goal.ProgressPercent, 0, 99);
    }

    [Fact]
    public async Task SundayStartedMondayCompletedWalk_BelongsToCompletionWeek()
    {
        await using var db = await CreateAsync();
        var mondayUtc = new DateTime(2026, 9, 20, 22, 0, 0, DateTimeKind.Utc);
        var walk = AddWalk(db, Owner, "Completed", mondayUtc.AddMinutes(20), 200);
        walk.StartedAt = mondayUtc.AddMinutes(-10);
        await db.SaveChangesAsync();
        _clock.SetUtcNow(mondayUtc.AddHours(1));

        var goals = await new WeeklyGoalsService(db, _clock).GetForUserAsync(Owner);
        Assert.Equal(1, goals.Goals.Single(goal => goal.Key == "walks").Current);
        Assert.Equal(200, goals.Goals.Single(goal => goal.Key == "distance").Current);
    }

    [Fact]
    public async Task Photo_MustBeUploadedThisWeekToAnOwnedWalkCompletedThisWeek()
    {
        await using var db = await CreateAsync();
        var mondayUtc = new DateTime(2026, 9, 20, 22, 0, 0, DateTimeKind.Utc);
        var current = AddWalk(db, Owner, "Completed", mondayUtc.AddMinutes(20), 200);
        current.StartedAt = mondayUtc.AddMinutes(-10);
        var old = AddWalk(db, Owner, "Completed", mondayUtc.AddTicks(-1), 200);
        var interrupted = AddWalk(db, Owner, "Interrupted", mondayUtc.AddMinutes(20), 200);
        var foreign = AddWalk(db, Other, "Completed", mondayUtc.AddMinutes(20), 200);
        await db.SaveChangesAsync();
        db.WalkPhotos.AddRange(
            Photo(current, Owner, mondayUtc.AddMinutes(-5)),
            Photo(current, Owner, mondayUtc.AddDays(7)),
            Photo(old, Owner, mondayUtc.AddMinutes(30)),
            Photo(interrupted, Owner, mondayUtc.AddMinutes(30)),
            Photo(foreign, Other, mondayUtc.AddMinutes(30)));
        await db.SaveChangesAsync();
        _clock.SetUtcNow(mondayUtc.AddHours(1));
        var service = new WeeklyGoalsService(db, _clock);
        Assert.Equal(0, (await service.GetForUserAsync(Owner)).Goals.Single(goal => goal.Key == "photo").Current);

        db.WalkPhotos.AddRange(Photo(current, Owner, mondayUtc.AddMinutes(30)), Photo(current, Owner, mondayUtc.AddMinutes(40)));
        await db.SaveChangesAsync();
        Assert.Equal(1, (await service.GetForUserAsync(Owner)).Goals.Single(goal => goal.Key == "photo").Current);
    }

    [Theory]
    [InlineData(1, "1 sprehod")]
    [InlineData(2, "2 sprehoda")]
    [InlineData(3, "3 sprehodi")]
    [InlineData(4, "4 sprehodi")]
    [InlineData(5, "5 sprehodov")]
    [InlineData(11, "11 sprehodov")]
    [InlineData(102, "102 sprehoda")]
    public void WalkCount_UsesNaturalSlovenianForms(int count, string expected) =>
        Assert.Equal(expected, WeeklyGoalsWording.Walks(count));

    [Fact]
    public void ViewModel_CapsVisualProgressPicksNearestGoalAndHandlesAllComplete()
    {
        var result = new WeeklyGoalsViewModel
        {
            Goals =
            [
                new("walks", "Sprehodi", "", "", 4, 3, "4 / 3", ""),
                new("distance", "Kilometri", "", "", 4_500, 5_000, "", ""),
                new("photo", "Fotografija", "", "", 0, 1, "", "")
            ]
        };
        Assert.Equal(100, result.Goals[0].ProgressPercent);
        Assert.Equal(90, result.Goals[1].ProgressPercent);
        Assert.Equal("distance", result.NextGoal?.Key);
        Assert.False(result.AllComplete);
        Assert.True(result.HasActivity);

        result = new WeeklyGoalsViewModel { Goals = result.Goals.Select(goal => goal with { Current = goal.Target }).ToArray() };
        Assert.True(result.AllComplete);
        Assert.Null(result.NextGoal);
    }

    [Fact]
    public void NextGoal_EqualRatiosKeepFixedOrder()
    {
        var goals = new WeeklyGoalsViewModel
        {
            Goals =
            [
                new("photo", "", "", "", 0, 1, "", ""),
                new("distance", "", "", "", 0, 5000, "", ""),
                new("walks", "", "", "", 0, 3, "", "")
            ]
        };
        Assert.Equal("walks", goals.NextGoal?.Key);
        Assert.Equal("Doseženi cilji: 0/3", goals.CompletedSummary);
        Assert.False(goals.HasActivity);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteProgress_NeverCompletesOrCreatesInvalidVisualPercent(double current)
    {
        var goal = new WeeklyGoalItem("distance", "", "", "", current, 5000, "", "");
        Assert.False(goal.IsComplete);
        Assert.Equal(0, goal.ProgressPercent);
    }

    [Fact]
    public async Task GoalsPage_IgnoresUserIdQueryAndUsesAuthenticatedOwner()
    {
        var service = new RecordingGoalsService();
        var controller = new WeeklyGoalsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, Owner)], "Test"))
                }
            }
        };
        controller.Request.QueryString = new QueryString("?userId=other");

        Assert.IsType<ViewResult>(await controller.Index());
        Assert.Equal(Owner, service.RequestedUserId);
    }

    private async Task<ApplicationDbContext> CreateAsync()
    {
        var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = Owner, UserName = "owner" }, new ApplicationUser { Id = Other, UserName = "other" });
        db.Dogs.AddRange(new Dog { Id = 1, OwnerId = Owner, Name = "Floyd" }, new Dog { Id = 2, OwnerId = Other, Name = "Rex" });
        await db.SaveChangesAsync();
        return db;
    }

    private static Walk AddWalk(ApplicationDbContext db, string owner, string status, DateTime endedAt, double meters, double plannedKm = 0)
    {
        var walk = new Walk { OwnerId = owner, DogId = owner == Owner ? 1 : 2, Status = status,
            StartedAt = endedAt.AddMinutes(-30), EndedAt = status == "Active" ? null : endedAt, DistanceMeters = meters };
        if (plannedKm > 0)
            walk.PlannedWalk = new PlannedWalk { OwnerId = owner, DogId = walk.DogId, EstimatedDistanceKm = plannedKm };
        db.Walks.Add(walk);
        return walk;
    }

    private static WalkPhoto Photo(Walk walk, string userId, DateTime createdAt) => new()
    {
        WalkId = walk.Id, UserId = userId, ImageUrl = "test-photo", CreatedAt = createdAt
    };

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_path};Default Timeout=15;Pooling=False").Options);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class RecordingGoalsService : IWeeklyGoalsService
    {
        public string? RequestedUserId { get; private set; }
        public Task<WeeklyGoalsViewModel> GetForUserAsync(string userId)
        {
            RequestedUserId = userId;
            return Task.FromResult(new WeeklyGoalsViewModel());
        }
    }
}
