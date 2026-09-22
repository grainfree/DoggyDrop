using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class AchievementReconciliationRunnerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"doggydrop-reconciliation-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Run_ProcessesMultipleUsersIncludingUserWithoutActivity()
    {
        await SeedUsersAsync("a-user", "b-user", "c-user");
        await using var context = Context();
        context.Dogs.AddRange(
            new Dog { OwnerId = "a-user", Name = "Luna", CreatedAt = Utc(1) },
            new Dog { OwnerId = "c-user", Name = "Max", CreatedAt = Utc(2) });
        await context.SaveChangesAsync();

        var result = await Runner(context).RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, result.UsersDiscovered);
        Assert.Equal(3, result.UsersProcessed);
        Assert.Equal(3, result.UsersSucceeded);
        Assert.Equal(2, result.AchievementsInserted);
        Assert.Equal(2, await context.UserAchievements.CountAsync());
    }

    [Fact]
    public async Task Run_PreservesExistingOwnershipAndUnlockedAt()
    {
        var unlockedAt = Utc(3);
        await SeedUsersAsync("a-user");
        await using var context = Context();
        context.Dogs.Add(new Dog { OwnerId = "a-user", Name = "Luna", CreatedAt = Utc(1) });
        context.UserAchievements.Add(new UserAchievement
        {
            UserId = "a-user",
            AchievementKey = UserAchievementCatalog.DogParent,
            UnlockedAt = unlockedAt,
            CreatedAt = unlockedAt
        });
        await context.SaveChangesAsync();

        var result = await Runner(context).RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.AchievementsInserted);
        Assert.Equal(1, result.AchievementsAlreadyOwned);
        Assert.Equal(unlockedAt, (await context.UserAchievements.SingleAsync()).UnlockedAt);
    }

    [Fact]
    public async Task Run_IsIdempotentAndHasNoProgressionSideEffects()
    {
        await SeedUsersAsync("a-user");
        await using var context = Context();
        var dog = new Dog { OwnerId = "a-user", Name = "Luna", CreatedAt = Utc(1) };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        context.Walks.Add(new Walk
        {
            OwnerId = "a-user",
            DogId = dog.Id,
            Status = "Completed",
            StartedAt = Utc(2),
            EndedAt = Utc(2).AddHours(1),
            DistanceMeters = 1000
        });
        context.UserGamificationProfiles.Add(new UserGamificationProfile { UserId = "a-user", TotalXp = 55, Level = 1 });
        context.UserStreaks.Add(new UserStreak
        {
            UserId = "a-user",
            StreakType = GamificationStreakConstants.Walk,
            CurrentDays = 2,
            LongestDays = 2,
            LastActivityDate = new DateOnly(2026, 1, 2)
        });
        await context.SaveChangesAsync();

        var sourceWalk = await context.Walks.AsNoTracking().SingleAsync();
        var first = await Runner(context).RunAsync();
        var second = await Runner(context).RunAsync();

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, first.AchievementsInserted);
        Assert.Equal(0, second.AchievementsInserted);
        Assert.Equal(2, second.AchievementsAlreadyOwned);
        Assert.Equal(2, await context.UserAchievements.CountAsync());
        Assert.Empty(await context.UserXpEvents.ToListAsync());
        Assert.Empty(await context.UserNotifications.ToListAsync());
        Assert.Equal(55, (await context.UserGamificationProfiles.SingleAsync()).TotalXp);
        Assert.Equal(2, (await context.UserStreaks.SingleAsync()).CurrentDays);
        var walkAfter = await context.Walks.AsNoTracking().SingleAsync();
        Assert.Equal(sourceWalk.EndedAt, walkAfter.EndedAt);
        Assert.Equal(sourceWalk.DistanceMeters, walkAfter.DistanceMeters);
    }

    [Fact]
    public async Task Run_RollsBackFailedUserAndContinuesOtherUsers()
    {
        await SeedUsersAsync("a-good", "b-fails", "c-good");
        await using var context = Context();
        var service = new ControlledAchievementService(
            context,
            "b-fails",
            new RecoverableAchievementReconciliationException("Simulated recoverable user failure."));

        var result = await Runner(context, service).RunAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(3, result.UsersProcessed);
        Assert.Equal(2, result.UsersSucceeded);
        Assert.Equal(1, result.UsersFailed);
        Assert.Equal(2, await context.UserAchievements.CountAsync());
        Assert.False(await context.UserAchievements.AnyAsync(item => item.UserId == "b-fails"));
        Assert.True(await context.UserAchievements.AnyAsync(item => item.UserId == "c-good"));
    }

    [Fact]
    public async Task Run_ProcessesEveryUserExactlyOnceAcrossBatchBoundaries()
    {
        var userIds = Enumerable.Range(0, 205).Select(index => $"user-{index:D3}").ToArray();
        await SeedUsersAsync(userIds);
        await using var context = Context();
        var service = new ControlledAchievementService(context);

        var result = await Runner(context, service).RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(205, result.UsersDiscovered);
        Assert.Equal(205, result.UsersProcessed);
        Assert.Equal(205, result.UsersSucceeded);
        Assert.Equal(205, result.AchievementsInserted);
        Assert.Equal(205, service.ProcessedUserIds.Count);
        Assert.Equal(205, service.ProcessedUserIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(userIds, service.ProcessedUserIds);
        Assert.Equal(205, await context.UserAchievements.CountAsync());
        Assert.Contains("user-099", service.ProcessedUserIds);
        Assert.Contains("user-100", service.ProcessedUserIds);
        Assert.Contains("user-199", service.ProcessedUserIds);
        Assert.Contains("user-200", service.ProcessedUserIds);
    }

    [Fact]
    public async Task Run_KeysetPopulationDoesNotSkipInitialUsersWhenEarlierUserIsInserted()
    {
        var userIds = Enumerable.Range(0, 205).Select(index => $"user-{index:D3}").ToArray();
        await SeedUsersAsync(userIds);
        await using var context = Context();
        var service = new ControlledAchievementService(
            context,
            onCall: async (_, callCount) =>
            {
                if (callCount != 100) return;
                context.Users.Add(new ApplicationUser
                {
                    Id = "000-late-user",
                    UserName = "late@example.test",
                    NormalizedUserName = "LATE@EXAMPLE.TEST"
                });
                await context.SaveChangesAsync();
            });

        var result = await Runner(context, service).RunAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(205, result.UsersDiscovered);
        Assert.Equal(205, result.UsersProcessed);
        Assert.Equal(userIds, service.ProcessedUserIds);
        Assert.DoesNotContain("000-late-user", service.ProcessedUserIds);
        Assert.Equal(205, await context.UserAchievements.CountAsync());
    }

    [Fact]
    public async Task Run_UnexpectedExceptionRollsBackAndStopsImmediately()
    {
        await SeedUsersAsync("a-good", "b-fails", "c-not-processed");
        await using var context = Context();
        var service = new ControlledAchievementService(
            context,
            "b-fails",
            new InvalidOperationException("Simulated unexpected reconciliation bug."));

        var result = await Runner(context, service).RunAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.SystemicFailure);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(2, result.UsersProcessed);
        Assert.Equal(1, result.UsersSucceeded);
        Assert.Equal(1, result.UsersFailed);
        Assert.Equal(["a-good", "b-fails"], service.ProcessedUserIds);
        Assert.True(await context.UserAchievements.AnyAsync(item => item.UserId == "a-good"));
        Assert.False(await context.UserAchievements.AnyAsync(item => item.UserId == "b-fails"));
        Assert.False(await context.UserAchievements.AnyAsync(item => item.UserId == "c-not-processed"));
    }

    [Fact]
    public async Task Run_DatabaseFailureRollsBackAndStopsImmediately()
    {
        await SeedUsersAsync("a-good", "b-timeout", "c-not-processed");
        await using var context = Context();
        var service = new ControlledAchievementService(
            context,
            "b-timeout",
            new TimeoutException("Simulated database timeout."));

        var result = await Runner(context, service).RunAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.SystemicFailure);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(2, result.UsersProcessed);
        Assert.Equal(["a-good", "b-timeout"], service.ProcessedUserIds);
        Assert.True(await context.UserAchievements.AnyAsync(item => item.UserId == "a-good"));
        Assert.False(await context.UserAchievements.AnyAsync(item => item.UserId == "b-timeout"));
        Assert.False(await context.UserAchievements.AnyAsync(item => item.UserId == "c-not-processed"));
    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "reconcile-achievements" }, true)]
    [InlineData(new[] { "RECONCILE-ACHIEVEMENTS" }, true)]
    [InlineData(new[] { "reconcile-achievements", "extra" }, false)]
    [InlineData(new[] { "serve" }, false)]
    public void CommandDispatch_IsExplicitOnly(string[] args, bool expected) =>
        Assert.Equal(expected, AchievementReconciliationCommand.IsRequested(args));

    private AchievementReconciliationRunner Runner(
        ApplicationDbContext context,
        IUserAchievementService? service = null) =>
        new(
            context,
            service ?? new UserAchievementService(context, new NoOpNotifications()),
            NullLogger<AchievementReconciliationRunner>.Instance);

    private async Task SeedUsersAsync(params string[] userIds)
    {
        await using var context = Context();
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(userIds.Select(id => new ApplicationUser
        {
            Id = id,
            UserName = $"{id}@example.test",
            NormalizedUserName = $"{id}@example.test".ToUpperInvariant()
        }));
        await context.SaveChangesAsync();
    }

    private ApplicationDbContext Context() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=15;Pooling=False")
            .Options);

    private static DateTime Utc(int day) => new(2026, 1, day, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private sealed class NoOpNotifications : INotificationService
    {
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null) => Task.CompletedTask;
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) => Task.CompletedTask;
    }

    private sealed class ControlledAchievementService : IUserAchievementService
    {
        private readonly ApplicationDbContext _context;
        private readonly string? _failingUser;
        private readonly Exception? _failure;
        private readonly Func<string, int, Task>? _onCall;

        public ControlledAchievementService(
            ApplicationDbContext context,
            string? failingUser = null,
            Exception? failure = null,
            Func<string, int, Task>? onCall = null)
        {
            _context = context;
            _failingUser = failingUser;
            _failure = failure;
            _onCall = onCall;
        }

        public List<string> ProcessedUserIds { get; } = [];

        public Task<IReadOnlyList<UserAchievement>> GetOwnedAsync(string userId) => throw new NotSupportedException();
        public Task<bool> IsOwnedAsync(string userId, string achievementKey) => throw new NotSupportedException();
        public Task<AchievementUnlockResult> TryUnlockAsync(string userId, string achievementKey, DateTime unlockedAt, string? sourceType = null, string? sourceId = null, bool notify = true) => throw new NotSupportedException();

        public async Task<IReadOnlyList<AchievementUnlockResult>> ReconcileUserAsync(string userId)
        {
            ProcessedUserIds.Add(userId);
            if (_onCall != null) await _onCall(userId, ProcessedUserIds.Count);

            var achievements = new[]
            {
                UserAchievementCatalog.DogParent,
                UserAchievementCatalog.WalkFirst,
                UserAchievementCatalog.Walk10Km,
                UserAchievementCatalog.Walk100Km
            };
            var count = userId == _failingUser && _failure != null ? achievements.Length : 1;
            for (var index = 0; index < count; index++)
            {
                _context.UserAchievements.Add(new UserAchievement
                {
                    UserId = userId,
                    AchievementKey = achievements[index],
                    UnlockedAt = Utc(1),
                    CreatedAt = Utc(1)
                });
            }
            await _context.SaveChangesAsync();

            if (userId == _failingUser && _failure != null)
            {
                throw _failure;
            }

            return
            [
                new AchievementUnlockResult
                {
                    AchievementKey = achievements[0],
                    NewlyUnlocked = true,
                    UnlockedAt = Utc(1)
                }
            ];
        }
    }
}
