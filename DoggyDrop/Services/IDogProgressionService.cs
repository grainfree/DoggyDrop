using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public interface IDogProgressionService
    {
        Task<DogProgressionProfile> EnsureProfileAsync(int dogId);

        Task<DogXpEvent?> AwardXpAsync(
            int dogId,
            string activityType,
            int xpAmount,
            DogProgressionStatBoost? stats = null,
            string? referenceType = null,
            string? referenceId = null,
            string? description = null);

        DogProgressionLevelInfo CalculateLevelInfo(int totalXp);
    }
}
