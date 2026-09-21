using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public interface IGamificationService
    {
        Task<UserGamificationProfile> EnsureProfileAsync(string userId);

        Task<UserXpEvent?> AwardXpAsync(
            string? userId,
            string activityType,
            int xpAmount,
            string? referenceType = null,
            string? referenceId = null,
            string? description = null);

        Task<UserXpEvent?> AwardDailyLoginAsync(string? userId);

        Task<UserStreak?> RecordStreakActivityAsync(string? userId, string streakType);
        Task<UserStreak?> RecordStreakActivityAtAsync(string? userId, string streakType, DateTime utcEventInstant);
        GamificationStreakInfo GetEffectiveStreak(UserStreak? streak, string streakType);
        Task<GamificationStreakInfo> GetStreakAsync(string userId, string streakType);

        Task<IReadOnlyList<GamificationStreakInfo>> GetStreaksAsync(string userId);

        Task<GamificationLevelInfo> GetLevelInfoAsync(string userId);

        GamificationLevelInfo CalculateLevelInfo(int totalXp);
    }
}
