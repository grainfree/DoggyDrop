using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services
{
    public class DogProgressionService : IDogProgressionService
    {
        private readonly ApplicationDbContext _context;

        public DogProgressionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DogProgressionProfile> EnsureProfileAsync(int dogId)
        {
            var profile = await _context.DogProgressionProfiles
                .FirstOrDefaultAsync(item => item.DogId == dogId);

            if (profile != null)
            {
                return profile;
            }

            profile = new DogProgressionProfile
            {
                DogId = dogId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.DogProgressionProfiles.Add(profile);
            await _context.SaveChangesAsync();
            return profile;
        }

        public async Task<DogXpEvent?> AwardXpAsync(
            int dogId,
            string activityType,
            int xpAmount,
            DogProgressionStatBoost? stats = null,
            string? referenceType = null,
            string? referenceId = null,
            string? description = null)
        {
            if (dogId <= 0 || xpAmount <= 0 || string.IsNullOrWhiteSpace(activityType))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(referenceType) && !string.IsNullOrWhiteSpace(referenceId))
            {
                var alreadyAwarded = await _context.DogXpEvents.AnyAsync(item =>
                    item.DogId == dogId
                    && item.ActivityType == activityType
                    && item.ReferenceType == referenceType
                    && item.ReferenceId == referenceId);

                if (alreadyAwarded)
                {
                    return null;
                }
            }

            var profile = await EnsureProfileAsync(dogId);
            var xpEvent = new DogXpEvent
            {
                DogId = dogId,
                ActivityType = activityType,
                XpAmount = xpAmount,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                Description = description,
                OccurredAt = DateTime.UtcNow
            };

            _context.DogXpEvents.Add(xpEvent);
            profile.TotalXp += xpAmount;
            profile.Adventure += stats?.Adventure ?? 0;
            profile.Social += stats?.Social ?? 0;
            profile.Forest += stats?.Forest ?? 0;
            profile.City += stats?.City ?? 0;
            profile.Water += stats?.Water ?? 0;
            profile.Speed += stats?.Speed ?? 0;

            var levelInfo = CalculateLevelInfo(profile.TotalXp);
            profile.Level = levelInfo.Level;
            profile.DogClass = CalculateDogClass(profile);
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return xpEvent;
        }

        public DogProgressionLevelInfo CalculateLevelInfo(int totalXp)
        {
            var safeXp = Math.Max(0, totalXp);
            var level = Math.Max(1, (int)Math.Floor(Math.Sqrt(safeXp / 80d)) + 1);
            return new DogProgressionLevelInfo
            {
                TotalXp = safeXp,
                Level = level,
                CurrentLevelXp = RequiredXpForLevel(level),
                NextLevelXp = RequiredXpForLevel(level + 1)
            };
        }

        private static int RequiredXpForLevel(int level)
        {
            var safeLevel = Math.Max(1, level);
            return (safeLevel - 1) * (safeLevel - 1) * 80;
        }

        private static string CalculateDogClass(DogProgressionProfile profile)
        {
            var stats = new Dictionary<string, int>
            {
                ["Adventure"] = profile.Adventure,
                ["Social"] = profile.Social,
                ["Forest"] = profile.Forest,
                ["City"] = profile.City,
                ["Water"] = profile.Water,
                ["Speed"] = profile.Speed
            };

            return stats.OrderByDescending(item => item.Value).First().Key switch
            {
                "Speed" => "Speed Walker",
                "Water" => "Water Dog",
                "Forest" => "Trail Hunter",
                "Social" => "Social Pup",
                "Adventure" => "Explorer",
                _ => "Urban Sniffer"
            };
        }
    }
}
