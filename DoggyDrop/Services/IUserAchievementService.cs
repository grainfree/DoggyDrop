using DoggyDrop.Models;

namespace DoggyDrop.Services;

public interface IUserAchievementService
{
    Task<IReadOnlyList<UserAchievement>> GetOwnedAsync(string userId);
    Task<bool> IsOwnedAsync(string userId, string achievementKey);
    Task<AchievementUnlockResult> TryUnlockAsync(string userId, string achievementKey, DateTime unlockedAt, string? sourceType = null, string? sourceId = null, bool notify = true);
    Task<IReadOnlyList<AchievementUnlockResult>> ReconcileUserAsync(string userId);
}
