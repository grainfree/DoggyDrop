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

        Task<GamificationLevelInfo> GetLevelInfoAsync(string userId);

        GamificationLevelInfo CalculateLevelInfo(int totalXp);
    }
}
