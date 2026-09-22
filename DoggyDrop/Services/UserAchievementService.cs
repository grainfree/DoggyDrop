using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class UserAchievementService : IUserAchievementService
{
    private static readonly IReadOnlyDictionary<string, string[]> LegacyTitles =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [UserAchievementCatalog.WalkFirst] = ["First walk"],
            [UserAchievementCatalog.Walk10Km] = ["10 km walked"],
            [UserAchievementCatalog.Walk100Km] = ["100 km walked"],
            [UserAchievementCatalog.Bin10Submissions] = ["Added 10 bins"],
            [UserAchievementCatalog.Explorer5Places] = ["Visited 5 parks"]
        };

    private readonly ApplicationDbContext _context;
    private readonly INotificationService _notifications;

    public UserAchievementService(ApplicationDbContext context, INotificationService notifications)
    {
        _context = context;
        _notifications = notifications;
    }

    public async Task<IReadOnlyList<UserAchievement>> GetOwnedAsync(string userId) =>
        await _context.UserAchievements.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.UnlockedAt)
            .ToListAsync();

    public Task<bool> IsOwnedAsync(string userId, string achievementKey) =>
        _context.UserAchievements.AsNoTracking()
            .AnyAsync(item => item.UserId == userId && item.AchievementKey == achievementKey);

    public async Task<AchievementUnlockResult> TryUnlockAsync(
        string userId,
        string achievementKey,
        DateTime unlockedAt,
        string? sourceType = null,
        string? sourceId = null,
        bool notify = true)
    {
        _ = UserAchievementCatalog.Get(achievementKey);
        unlockedAt = unlockedAt.Kind switch
        {
            DateTimeKind.Utc => unlockedAt,
            DateTimeKind.Local => unlockedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(unlockedAt, DateTimeKind.Utc)
        };

        var createdAt = DateTime.UtcNow;
        int inserted;
        if (_context.Database.IsNpgsql())
        {
            inserted = await _context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "UserAchievements" ("UserId", "AchievementKey", "UnlockedAt", "SourceType", "SourceId", "CreatedAt")
                VALUES ({{userId}}, {{achievementKey}}, {{unlockedAt}}, {{sourceType}}, {{sourceId}}, {{createdAt}})
                ON CONFLICT ("UserId", "AchievementKey") DO NOTHING
                """);
        }
        else if (_context.Database.ProviderName?.Contains("Sqlite", StringComparison.Ordinal) == true)
        {
            inserted = await _context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "UserAchievements" ("UserId", "AchievementKey", "UnlockedAt", "SourceType", "SourceId", "CreatedAt")
                VALUES ({{userId}}, {{achievementKey}}, {{unlockedAt}}, {{sourceType}}, {{sourceId}}, {{createdAt}})
                ON CONFLICT ("UserId", "AchievementKey") DO NOTHING
                """);
        }
        else
        {
            throw new NotSupportedException($"Atomic achievement unlock is not configured for EF provider '{_context.Database.ProviderName}'.");
        }

        var owned = await _context.UserAchievements.AsNoTracking()
            .SingleAsync(item => item.UserId == userId && item.AchievementKey == achievementKey);
        var newlyUnlocked = inserted == 1;
        if (newlyUnlocked && notify)
        {
            var definition = UserAchievementCatalog.Get(achievementKey);
            await _notifications.CreateAsync(
                userId,
                $"Achievement:{achievementKey}",
                definition.DisplayName,
                definition.Description,
                "/Home/UserProfile");
        }

        return new AchievementUnlockResult
        {
            AchievementKey = achievementKey,
            NewlyUnlocked = newlyUnlocked,
            UnlockedAt = owned.UnlockedAt,
            SourceType = owned.SourceType,
            SourceId = owned.SourceId
        };
    }

    public async Task<IReadOnlyList<AchievementUnlockResult>> ReconcileUserAsync(string userId)
    {
        var candidates = new List<(string Key, DateTime At, string Type, string Id)>();
        var dogs = await _context.Dogs.AsNoTracking().Where(item => item.OwnerId == userId).OrderBy(item => item.CreatedAt).ToListAsync();
        if (dogs.FirstOrDefault() is { } firstDog)
            candidates.Add((UserAchievementCatalog.DogParent, firstDog.CreatedAt, nameof(Dog), firstDog.Id.ToString()));

        var walks = await _context.Walks.AsNoTracking()
            .Where(item => item.OwnerId == userId && item.Status == "Completed")
            .OrderBy(item => item.EndedAt ?? item.StartedAt).ThenBy(item => item.Id).ToListAsync();
        if (walks.FirstOrDefault() is { } firstWalk)
            candidates.Add((UserAchievementCatalog.WalkFirst, firstWalk.EndedAt ?? firstWalk.StartedAt, nameof(Walk), firstWalk.Id.ToString()));
        AddWalkCrossing(candidates, walks, 10, UserAchievementCatalog.Walk10Km);
        AddWalkCrossing(candidates, walks, 100, UserAchievementCatalog.Walk100Km);

        var bins = await _context.TrashBins.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.DateAdded).ThenBy(item => item.Id).ToListAsync();
        if (bins.Count >= 1) candidates.Add((UserAchievementCatalog.BinFirstSubmission, bins[0].DateAdded, nameof(TrashBin), bins[0].Id.ToString()));
        if (bins.Count >= 10) candidates.Add((UserAchievementCatalog.Bin10Submissions, bins[9].DateAdded, nameof(TrashBin), bins[9].Id.ToString()));

        var visits = await _context.DogParkVisits.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.VisitedAt).ThenBy(item => item.Id).ToListAsync();
        visits = visits.Where(visit => ParkLocationCatalog.Find(visit.PlaceKey) != null).ToList();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var fifth = visits.FirstOrDefault(visit => distinct.Add(visit.PlaceKey) && distinct.Count == 5);
        if (fifth != null) candidates.Add((UserAchievementCatalog.Explorer5Places, fifth.VisitedAt, nameof(DogParkVisit), fifth.Id.ToString()));

        var results = new List<AchievementUnlockResult>();
        foreach (var candidate in candidates)
        {
            var legacyAt = await FindLegacyNotificationTimeAsync(userId, candidate.Key);
            var historicalAt = legacyAt >= candidate.At ? legacyAt.Value : candidate.At;
            results.Add(await TryUnlockAsync(userId, candidate.Key, historicalAt, candidate.Type, candidate.Id, notify: false));
        }
        return results;
    }

    private async Task<DateTime?> FindLegacyNotificationTimeAsync(string userId, string key)
    {
        if (!LegacyTitles.TryGetValue(key, out var titles)) return null;
        return await _context.UserNotifications.AsNoTracking()
            .Where(item => item.UserId == userId && item.Type == "Achievement" && titles.Contains(item.Title))
            .OrderBy(item => item.CreatedAt).Select(item => (DateTime?)item.CreatedAt).FirstOrDefaultAsync();
    }

    private static void AddWalkCrossing(List<(string Key, DateTime At, string Type, string Id)> candidates, IReadOnlyList<Walk> walks, double targetKm, string key)
    {
        var distance = 0d;
        foreach (var walk in walks)
        {
            distance += walk.DistanceMeters / 1000d;
            if (distance < targetKm) continue;
            candidates.Add((key, walk.EndedAt ?? walk.StartedAt, nameof(Walk), walk.Id.ToString()));
            return;
        }
    }
}
