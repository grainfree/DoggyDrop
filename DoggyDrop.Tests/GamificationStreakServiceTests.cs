using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class GamificationStreakServiceTests
{
    private const string UserId = "streak-user";

    [Fact]
    public async Task FirstWalk_IsOneAndSafeToday()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc));
        var streak = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        var result = fixture.Service.GetEffectiveStreak(streak, GamificationStreakConstants.Walk);
        Assert.Equal(1, result.EffectiveCurrentDays);
        Assert.Equal(GamificationStreakState.SafeToday, result.State);
    }

    [Fact]
    public async Task NextLocalDay_Increments_AndSameDayDoesNot()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 1, 10, 22, 30, 0, DateTimeKind.Utc));
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 1, 11, 22, 30, 0, DateTimeKind.Utc));
        var streak = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        Assert.Equal(2, streak!.CurrentDays);
    }

    [Fact]
    public async Task Yesterday_IsAtRisk_AndOlderIsExpiredWithoutMutation()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));
        var streak = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 2, 11, 12, 0, 0, DateTimeKind.Utc));
        var atRisk = fixture.Service.GetEffectiveStreak(streak, GamificationStreakConstants.Walk);
        Assert.Equal(1, atRisk.EffectiveCurrentDays);
        Assert.Equal(GamificationStreakState.AtRiskToday, atRisk.State);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 2, 12, 12, 0, 0, DateTimeKind.Utc));
        var expired = fixture.Service.GetEffectiveStreak(streak, GamificationStreakConstants.Walk);
        Assert.Equal(0, expired.EffectiveCurrentDays);
        Assert.Equal(1, expired.StoredCurrentDays);
        Assert.Equal(GamificationStreakState.Expired, expired.State);
    }

    [Fact]
    public async Task ActivityAfterGap_ResetsAndPreservesLongest()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc));
        fixture.Context.UserStreaks.Add(new UserStreak { UserId = UserId, StreakType = GamificationStreakConstants.Walk, CurrentDays = 9, LongestDays = 9, LastActivityDate = fixture.Calendar.Today.AddDays(-3) });
        await fixture.Context.SaveChangesAsync();
        var streak = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        Assert.Equal(1, streak!.CurrentDays);
        Assert.Equal(9, streak.LongestDays);
    }

    [Theory]
    [InlineData("2026-01-10T22:30:00Z", "2026-01-10")]
    [InlineData("2026-01-10T23:30:00Z", "2026-01-11")]
    [InlineData("2026-07-10T21:30:00Z", "2026-07-10")]
    [InlineData("2026-07-10T22:30:00Z", "2026-07-11")]
    [InlineData("2026-03-29T00:30:00Z", "2026-03-29")]
    [InlineData("2026-03-29T01:30:00Z", "2026-03-29")]
    public void Calendar_UsesLjubljanaDate(string utc, string expected)
    {
        var calendar = new TestGamificationCalendar();
        Assert.Equal(DateOnly.Parse(expected), calendar.ToLocalDate(DateTime.Parse(utc).ToUniversalTime()));
    }

    [Fact]
    public async Task DstEndRepeatedHour_MapsToOneStreakDay()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc));
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
        var streak = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        Assert.Equal(new DateOnly(2026, 10, 25), fixture.Calendar.Today);
        Assert.Equal(1, streak!.CurrentDays);
    }

    [Fact]
    public async Task DailyLogin_UsesLocalDay_AndDoesNotNotifyDailyMilestone()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 7, 10, 22, 30, 0, DateTimeKind.Utc));
        var first = await fixture.Service.AwardDailyLoginAsync(UserId);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 7, 11, 20, 0, 0, DateTimeKind.Utc));
        var duplicate = await fixture.Service.AwardDailyLoginAsync(UserId);
        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.Empty(fixture.Notifications.Items);
    }

    [Fact]
    public async Task Explorer_RevisitProtectsAndIncrementsOnlyOnNextLocalDay()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Explorer);
        var sameDay = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Explorer);
        Assert.Equal(1, sameDay!.CurrentDays);
        fixture.Calendar.SetUtcNow(new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));
        var nextDay = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Explorer);
        Assert.Equal(2, nextDay!.CurrentDays);
        Assert.True(fixture.Service.GetEffectiveStreak(nextDay, GamificationStreakConstants.Explorer).IsSafeToday);
    }

    [Fact]
    public async Task UserFacingStreakCollection_DoesNotExposeDailyLogin()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Daily);
        var streaks = await fixture.Service.GetStreaksAsync(UserId);
        Assert.DoesNotContain(streaks, streak => streak.StreakType == GamificationStreakConstants.Daily);
    }

    [Fact]
    public async Task LegacyUtcDailyRewardForSameLocalDay_IsNotAwardedAgain()
    {
        var now = new DateTime(2026, 7, 10, 22, 30, 0, DateTimeKind.Utc);
        await using var fixture = await Fixture.CreateAsync(now);
        var profile = await fixture.Service.EnsureProfileAsync(UserId);
        profile.LastDailyLoginDate = new DateOnly(2026, 7, 10);
        fixture.Context.UserXpEvents.Add(new UserXpEvent
        {
            UserId = UserId,
            ActivityType = GamificationConstants.DailyLogin,
            XpAmount = GamificationConstants.DailyLoginXp,
            ReferenceType = "DailyLogin",
            ReferenceId = "2026-07-10",
            OccurredAt = new DateTime(2026, 7, 10, 22, 5, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();

        Assert.Null(await fixture.Service.AwardDailyLoginAsync(UserId));
        Assert.Single(await fixture.Context.UserXpEvents.ToListAsync());
        Assert.Equal(new DateOnly(2026, 7, 11), profile.LastDailyLoginDate);
    }

    [Fact]
    public async Task LegacyUtcRewardFromPreviousLocalDay_DoesNotBlockNewLocalDay()
    {
        var now = new DateTime(2026, 7, 10, 22, 30, 0, DateTimeKind.Utc);
        await using var fixture = await Fixture.CreateAsync(now);
        var profile = await fixture.Service.EnsureProfileAsync(UserId);
        profile.LastDailyLoginDate = new DateOnly(2026, 7, 10);
        fixture.Context.UserXpEvents.Add(new UserXpEvent
        {
            UserId = UserId,
            ActivityType = GamificationConstants.DailyLogin,
            XpAmount = GamificationConstants.DailyLoginXp,
            ReferenceType = "DailyLogin",
            ReferenceId = "2026-07-10",
            OccurredAt = new DateTime(2026, 7, 10, 10, 0, 0, DateTimeKind.Utc)
        });
        await fixture.Context.SaveChangesAsync();

        Assert.NotNull(await fixture.Service.AwardDailyLoginAsync(UserId));
        Assert.Equal(2, await fixture.Context.UserXpEvents.CountAsync());
    }

    [Fact]
    public async Task MalformedAndFutureStreaks_AreConservativelyExpired()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        var zero = fixture.Service.GetEffectiveStreak(new UserStreak { CurrentDays = 0, LastActivityDate = fixture.Calendar.Today }, GamificationStreakConstants.Walk);
        var future = fixture.Service.GetEffectiveStreak(new UserStreak { CurrentDays = 5, LastActivityDate = fixture.Calendar.Today.AddDays(1) }, GamificationStreakConstants.Walk);
        Assert.Equal(GamificationStreakState.Expired, zero.State);
        Assert.Equal(0, zero.EffectiveCurrentDays);
        Assert.False(zero.IsSafeToday);
        Assert.Equal(GamificationStreakState.Expired, future.State);
        Assert.Equal(0, future.EffectiveCurrentDays);
    }

    [Fact]
    public async Task EffectiveLongest_IsNeverBelowValidCurrentWithoutMutatingRow()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        var row = new UserStreak { CurrentDays = 5, LongestDays = 2, LastActivityDate = fixture.Calendar.Today };
        var result = fixture.Service.GetEffectiveStreak(row, GamificationStreakConstants.Walk);
        Assert.Equal(5, result.LongestDays);
        Assert.Equal(2, row.LongestDays);
    }

    [Fact]
    public async Task ExplicitFutureAndTooOldEventDates_AreRejected()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.RecordStreakActivityAtAsync(UserId, GamificationStreakConstants.Walk, new DateTime(2026, 6, 11, 22, 0, 0, DateTimeKind.Utc)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Service.RecordStreakActivityAtAsync(UserId, GamificationStreakConstants.Walk, new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task CapturedEventDateWinsWhenProcessingCrossesMidnight()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 7, 10, 22, 0, 1, DateTimeKind.Utc));
        var streak = await fixture.Service.RecordStreakActivityAtAsync(UserId, GamificationStreakConstants.Walk, new DateTime(2026, 7, 10, 21, 59, 59, DateTimeKind.Utc));
        Assert.Equal(new DateOnly(2026, 7, 10), streak!.LastActivityDate);
    }

    [Fact]
    public async Task ExpiredTenDayStreak_RestartsAsOneSafeReward()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        fixture.Context.UserStreaks.Add(new UserStreak { UserId = UserId, StreakType = GamificationStreakConstants.Explorer, CurrentDays = 10, LongestDays = 10, LastActivityDate = fixture.Calendar.Today.AddDays(-3) });
        await fixture.Context.SaveChangesAsync();
        var previous = await fixture.Service.GetStreakAsync(UserId, GamificationStreakConstants.Explorer);
        var row = await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Explorer);
        var current = fixture.Service.GetEffectiveStreak(row, GamificationStreakConstants.Explorer);
        var reward = new GamificationRewardBuilder().BuildStreakReward(current, previous.EffectiveCurrentDays, includeUnchanged: false);
        Assert.NotNull(reward);
        Assert.Equal(1, reward.CurrentDays);
        Assert.True(reward.Increased);
        Assert.Equal(GamificationStreakState.SafeToday, reward.State);
    }

    [Fact]
    public async Task DailyMilestoneIsSuppressedButWalkMilestoneRemains()
    {
        await using var fixture = await Fixture.CreateAsync(new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc));
        fixture.Context.UserStreaks.AddRange(
            new UserStreak { UserId = UserId, StreakType = GamificationStreakConstants.Daily, CurrentDays = 6, LongestDays = 6, LastActivityDate = fixture.Calendar.Today.AddDays(-1) },
            new UserStreak { UserId = UserId, StreakType = GamificationStreakConstants.Walk, CurrentDays = 6, LongestDays = 6, LastActivityDate = fixture.Calendar.Today.AddDays(-1) });
        await fixture.Context.SaveChangesAsync();
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Daily);
        await fixture.Service.RecordStreakActivityAsync(UserId, GamificationStreakConstants.Walk);
        Assert.DoesNotContain("Streak:Daily:7", fixture.Notifications.Items);
        Assert.Contains("Streak:Walk:7", fixture.Notifications.Items);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        public ApplicationDbContext Context { get; }
        public TestGamificationCalendar Calendar { get; }
        public RecordingNotifications Notifications { get; } = new();
        public GamificationService Service { get; }

        private Fixture(ApplicationDbContext context, TestGamificationCalendar calendar, string databasePath)
        {
            _databasePath = databasePath;
            Context = context;
            Calendar = calendar;
            Service = new GamificationService(context, Notifications, calendar);
        }

        public static async Task<Fixture> CreateAsync(DateTime utcNow)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"doggydrop-streak-{Guid.NewGuid():N}.db");
            var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False").Options);
            await context.Database.EnsureCreatedAsync();
            context.Users.Add(new ApplicationUser { Id = UserId, UserName = "streak@test" });
            await context.SaveChangesAsync();
            return new Fixture(context, new TestGamificationCalendar(utcNow), databasePath);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<string> Items { get; } = [];
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null) { Items.Add(type); return Task.CompletedTask; }
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) { Items.Add(type); return Task.CompletedTask; }
    }
}
