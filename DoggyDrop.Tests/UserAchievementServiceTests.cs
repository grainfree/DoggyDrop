using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class UserAchievementServiceTests : IDisposable
{
    private const string UserId = "achievement-user";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"doggydrop-achievements-{Guid.NewGuid():N}.db");

    public UserAchievementServiceTests()
    {
        using var context = Context();
        context.Database.EnsureCreated();
        context.Users.Add(new ApplicationUser { Id = UserId, UserName = "achievement@example.test", NormalizedUserName = "ACHIEVEMENT@EXAMPLE.TEST" });
        context.SaveChanges();
    }

    [Fact]
    public void Catalog_HasSevenUniqueStableKeys()
    {
        Assert.Equal(7, UserAchievementCatalog.All.Count);
        Assert.Equal(7, UserAchievementCatalog.All.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(UserAchievementCatalog.All, item => Assert.Matches("^[a-z0-9_]+$", item.Key));
    }

    [Fact]
    public async Task Unlock_IsPersistentAndRetryDoesNotDuplicateOrRenotify()
    {
        await using var context = Context();
        var notifications = new RecordingNotifications();
        var service = new UserAchievementService(context, notifications);
        var timestamp = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

        var first = await service.TryUnlockAsync(UserId, UserAchievementCatalog.WalkFirst, timestamp, nameof(Walk), "1");
        var retry = await service.TryUnlockAsync(UserId, UserAchievementCatalog.WalkFirst, timestamp.AddHours(1), nameof(Walk), "2");

        Assert.True(first.NewlyUnlocked);
        Assert.False(retry.NewlyUnlocked);
        Assert.Equal(timestamp, retry.UnlockedAt);
        Assert.Single(await context.UserAchievements.ToListAsync());
        Assert.Single(notifications.Items);
    }

    [Fact]
    public async Task ConcurrentUnlock_CreatesOneOwnershipAndOneNotification()
    {
        var notifications = new RecordingNotifications();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<AchievementUnlockResult> UnlockAsync()
        {
            await using var context = Context();
            var service = new UserAchievementService(context, notifications);
            await gate.Task;
            return await service.TryUnlockAsync(UserId, UserAchievementCatalog.Walk10Km, DateTime.UtcNow, nameof(Walk), "10");
        }

        var tasks = new[] { UnlockAsync(), UnlockAsync() };
        gate.SetResult();
        var results = await Task.WhenAll(tasks);

        await using var verification = Context();
        Assert.Single(await verification.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.Walk10Km).ToListAsync());
        Assert.Single(results, item => item.NewlyUnlocked);
        Assert.Single(notifications.Items);
    }

    [Fact]
    public void Model_HasRequiredUniqueOwnershipIndex()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(UserAchievement))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(UserAchievement.UserId), nameof(UserAchievement.AchievementKey)]));
    }

    [Fact]
    public void Presentation_UsesOwnershipForUnlockAndExposesDateEvenWhenProgressDrops()
    {
        var unlockedAt = new DateTime(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
        var items = UserAchievementPresentationBuilder.Build(
            new UserAchievementProgress(0, 1, 9.5, 0, 0),
            [new UserAchievement { UserId = UserId, AchievementKey = UserAchievementCatalog.Walk10Km, UnlockedAt = unlockedAt }]);

        var achievement = Assert.Single(items, item => item.Key == UserAchievementCatalog.Walk10Km);
        Assert.True(achievement.IsUnlocked);
        Assert.Equal(unlockedAt, achievement.UnlockedAt);
        Assert.Equal(100, achievement.ProgressPercent);
        Assert.Equal("Odklenjeno", achievement.ProgressText);
    }

    [Fact]
    public async Task OwnedAchievement_RemainsAfterSourceDataIsDeleted()
    {
        await using var context = Context();
        var dog = new Dog { OwnerId = UserId, Name = "Bobi", CreatedAt = DateTime.UtcNow };
        context.Dogs.Add(dog);
        await context.SaveChangesAsync();
        var service = new UserAchievementService(context, new RecordingNotifications());
        await service.TryUnlockAsync(UserId, UserAchievementCatalog.DogParent, dog.CreatedAt, nameof(Dog), dog.Id.ToString());

        context.Dogs.Remove(dog);
        await context.SaveChangesAsync();

        Assert.True(await service.IsOwnedAsync(UserId, UserAchievementCatalog.DogParent));
    }

    [Fact]
    public async Task Reconcile_BackfillsAllProvableAchievementsWithCrossingTimesWithoutRewards()
    {
        await using var context = Context();
        var notifications = new RecordingNotifications();
        var start = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        context.Dogs.Add(new Dog { Id = 10, OwnerId = UserId, Name = "Luna", CreatedAt = start });
        context.Walks.AddRange(
            new Walk { Id = 20, OwnerId = UserId, DogId = 10, Status = "Completed", StartedAt = start.AddDays(1), EndedAt = start.AddDays(1).AddHours(1), DistanceMeters = 6000 },
            new Walk { Id = 21, OwnerId = UserId, DogId = 10, Status = "Completed", StartedAt = start.AddDays(2), EndedAt = start.AddDays(2).AddHours(1), DistanceMeters = 95000 });
        for (var i = 0; i < 10; i++) context.TrashBins.Add(new TrashBin { Name = $"Koš {i}", UserId = UserId, DateAdded = start.AddDays(3 + i), Latitude = 46, Longitude = 15 });
        for (var i = 0; i < 5; i++) context.DogParkVisits.Add(new DogParkVisit { DogId = 10, UserId = UserId, PlaceKey = ParkLocationCatalog.All[i].PlaceKey, ParkName = ParkLocationCatalog.All[i].Name, VisitedAt = start.AddDays(20 + i) });
        await context.SaveChangesAsync();

        var service = new UserAchievementService(context, notifications);
        var results = await service.ReconcileUserAsync(UserId);
        var second = await service.ReconcileUserAsync(UserId);

        Assert.Equal(7, await context.UserAchievements.CountAsync());
        Assert.Equal(7, results.Count(item => item.NewlyUnlocked));
        Assert.All(second, item => Assert.False(item.NewlyUnlocked));
        Assert.Empty(await context.UserXpEvents.ToListAsync());
        Assert.Empty(notifications.Items);
        Assert.Equal(start.AddDays(2).AddHours(1), (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.Walk10Km)).UnlockedAt);
        Assert.Equal(start.AddDays(2).AddHours(1), (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.Walk100Km)).UnlockedAt);
        Assert.Equal(start.AddDays(24), (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.Explorer5Places)).UnlockedAt);
        Assert.Equal(start.AddDays(12), (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.Bin10Submissions)).UnlockedAt);
    }

    [Fact]
    public async Task Reconcile_UsesLegacyNotificationOnlyForHistoricalTimestamp()
    {
        await using var context = Context();
        var walkAt = new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc);
        var notificationAt = walkAt.AddMinutes(2);
        context.Dogs.Add(new Dog { Id = 30, OwnerId = UserId, Name = "Max", CreatedAt = walkAt.AddDays(-1) });
        context.Walks.Add(new Walk { OwnerId = UserId, DogId = 30, Status = "Completed", StartedAt = walkAt.AddHours(-1), EndedAt = walkAt, DistanceMeters = 1000 });
        context.UserNotifications.Add(new UserNotification { UserId = UserId, Type = "Achievement", Title = "First walk", Body = "legacy", CreatedAt = notificationAt });
        await context.SaveChangesAsync();

        var service = new UserAchievementService(context, new RecordingNotifications());
        await service.ReconcileUserAsync(UserId);

        Assert.Equal(notificationAt, (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.WalkFirst)).UnlockedAt);
    }

    [Fact]
    public async Task Reconcile_IgnoresLegacyNotificationThatPredatesQualification()
    {
        await using var context = Context();
        var walkAt = new DateTime(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc);
        context.Dogs.Add(new Dog { Id = 31, OwnerId = UserId, Name = "Max", CreatedAt = walkAt.AddDays(-1) });
        context.Walks.Add(new Walk { OwnerId = UserId, DogId = 31, Status = "Completed", StartedAt = walkAt.AddHours(-1), EndedAt = walkAt, DistanceMeters = 1000 });
        context.UserNotifications.Add(new UserNotification { UserId = UserId, Type = "Achievement", Title = "First walk", Body = "legacy", CreatedAt = walkAt.AddDays(-2) });
        await context.SaveChangesAsync();

        await new UserAchievementService(context, new RecordingNotifications()).ReconcileUserAsync(UserId);

        Assert.Equal(walkAt, (await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.WalkFirst)).UnlockedAt);
    }

    [Fact]
    public async Task Reconcile_ExplorerCountsOnlyFifthDistinctCanonicalPlace()
    {
        await using var context = Context();
        var start = new DateTime(2026, 5, 1, 8, 0, 0, DateTimeKind.Utc);
        context.Dogs.Add(new Dog { Id = 40, OwnerId = UserId, Name = "Luna", CreatedAt = start });
        var keys = new[]
        {
            ParkLocationCatalog.All[0].PlaceKey,
            ParkLocationCatalog.All[1].PlaceKey,
            "fake-one",
            ParkLocationCatalog.All[2].PlaceKey,
            "obsolete-place",
            ParkLocationCatalog.All[3].PlaceKey,
            ParkLocationCatalog.All[4].PlaceKey
        };
        for (var i = 0; i < keys.Length; i++)
            context.DogParkVisits.Add(new DogParkVisit { DogId = 40, UserId = UserId, PlaceKey = keys[i], ParkName = keys[i], VisitedAt = start.AddDays(i) });
        await context.SaveChangesAsync();

        await new UserAchievementService(context, new RecordingNotifications()).ReconcileUserAsync(UserId);

        var owned = await context.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.Explorer5Places);
        Assert.Equal(start.AddDays(6), owned.UnlockedAt);
        Assert.Equal((await context.DogParkVisits.SingleAsync(item => item.PlaceKey == keys[6])).Id.ToString(), owned.SourceId);
    }

    [Fact]
    public async Task Reconcile_InvalidPlacesCannotCompleteExplorerAchievement()
    {
        await using var context = Context();
        var start = new DateTime(2026, 6, 1, 8, 0, 0, DateTimeKind.Utc);
        context.Dogs.Add(new Dog { Id = 41, OwnerId = UserId, Name = "Luna", CreatedAt = start });
        var keys = ParkLocationCatalog.All.Take(4).Select(item => item.PlaceKey).Concat(["fake-one", "fake-two"]).ToList();
        for (var i = 0; i < keys.Count; i++)
            context.DogParkVisits.Add(new DogParkVisit { DogId = 41, UserId = UserId, PlaceKey = keys[i], ParkName = keys[i], VisitedAt = start.AddDays(i) });
        await context.SaveChangesAsync();

        await new UserAchievementService(context, new RecordingNotifications()).ReconcileUserAsync(UserId);

        Assert.False(await context.UserAchievements.AnyAsync(item => item.AchievementKey == UserAchievementCatalog.Explorer5Places));
    }

    [Fact]
    public void WalkCategoryPresentationContainsOnlyWalkAchievements()
    {
        var items = UserAchievementPresentationBuilder.Build(new UserAchievementProgress(1, 1, 12, 10, 5), [], "walks");
        Assert.Equal([UserAchievementCatalog.WalkFirst, UserAchievementCatalog.Walk10Km, UserAchievementCatalog.Walk100Km], items.Select(item => item.Key));
    }

    [Fact]
    public async Task DeletedNotificationDoesNotRecreateOwnedAchievementNotification()
    {
        await using var context = Context();
        var notifications = new NotificationService(context);
        var service = new UserAchievementService(context, notifications);
        await service.TryUnlockAsync(UserId, UserAchievementCatalog.Walk10Km, DateTime.UtcNow, nameof(Walk), "1");
        context.UserNotifications.RemoveRange(context.UserNotifications);
        await context.SaveChangesAsync();

        var retry = await service.TryUnlockAsync(UserId, UserAchievementCatalog.Walk10Km, DateTime.UtcNow.AddDays(1), nameof(Walk), "2");

        Assert.False(retry.NewlyUnlocked);
        Assert.Empty(await context.UserNotifications.ToListAsync());
        Assert.Single(await context.UserAchievements.Where(item => item.AchievementKey == UserAchievementCatalog.Walk10Km).ToListAsync());
    }

    [Fact]
    public async Task ConcurrentReconciliationCreatesOneOwnershipWithoutRewardsOrTimestampOverwrite()
    {
        var createdAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
        await using (var setup = Context())
        {
            setup.Dogs.Add(new Dog { OwnerId = UserId, Name = "Luna", CreatedAt = createdAt });
            await setup.SaveChangesAsync();
        }

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = new RecordingNotifications();
        async Task ReconcileAsync()
        {
            await using var context = Context();
            await gate.Task;
            await new UserAchievementService(context, notifications).ReconcileUserAsync(UserId);
        }

        var calls = new[] { ReconcileAsync(), ReconcileAsync() };
        gate.SetResult();
        await Task.WhenAll(calls);

        await using var verification = Context();
        var owned = await verification.UserAchievements.SingleAsync(item => item.AchievementKey == UserAchievementCatalog.DogParent);
        Assert.Equal(createdAt, owned.UnlockedAt);
        Assert.Empty(await verification.UserXpEvents.ToListAsync());
        Assert.Empty(notifications.Items);
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_databasePath};Default Timeout=15;Pooling=False").Options);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
    }

    private sealed class RecordingNotifications : INotificationService
    {
        private readonly List<string> _items = [];
        private readonly object _sync = new();
        public IReadOnlyList<string> Items { get { lock (_sync) return _items.ToList(); } }
        public Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null) { lock (_sync) _items.Add(type); return Task.CompletedTask; }
        public Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24) => CreateAsync(userId, type, title, body, linkUrl);
    }
}
