using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services
{
    public class GamificationService : IGamificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;

        public GamificationService(ApplicationDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<UserGamificationProfile> EnsureProfileAsync(string userId)
        {
            var profile = await _context.UserGamificationProfiles
                .FirstOrDefaultAsync(item => item.UserId == userId);

            if (profile != null)
            {
                return profile;
            }

            profile = new UserGamificationProfile
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.UserGamificationProfiles.Add(profile);
            await _context.SaveChangesAsync();
            return profile;
        }

        public async Task<UserXpEvent?> AwardXpAsync(
            string? userId,
            string activityType,
            int xpAmount,
            string? referenceType = null,
            string? referenceId = null,
            string? description = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || xpAmount <= 0 || string.IsNullOrWhiteSpace(activityType))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(referenceType) && !string.IsNullOrWhiteSpace(referenceId))
            {
                var alreadyAwarded = await _context.UserXpEvents.AnyAsync(item =>
                    item.UserId == userId
                    && item.ActivityType == activityType
                    && item.ReferenceType == referenceType
                    && item.ReferenceId == referenceId);

                if (alreadyAwarded)
                {
                    return null;
                }
            }

            var profile = await EnsureProfileAsync(userId);
            var previousLevel = profile.Level;
            var now = DateTime.UtcNow;
            var xpEvent = new UserXpEvent
            {
                UserId = userId,
                ActivityType = activityType,
                XpAmount = xpAmount,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                Description = description,
                OccurredAt = now
            };

            _context.UserXpEvents.Add(xpEvent);
            profile.TotalXp += xpAmount;
            var levelInfo = CalculateLevelInfo(profile.TotalXp);
            profile.Level = levelInfo.Level;
            profile.Title = levelInfo.Title;
            profile.UpdatedAt = now;

            await _context.SaveChangesAsync();

            if (profile.Level > previousLevel)
            {
                await _notificationService.CreateUniqueRecentAsync(
                    userId,
                    $"LevelUp:{profile.Level}",
                    $"Level {profile.Level}: {profile.Title}",
                    $"Dosegel si level {profile.Level} in naslov {profile.Title}.",
                    "/Home/UserProfile",
                    withinHours: 24 * 365);
            }

            return xpEvent;
        }

        public async Task<UserXpEvent?> AwardDailyLoginAsync(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var profile = await EnsureProfileAsync(userId);
            if (profile.LastDailyLoginDate == today)
            {
                return null;
            }

            if (profile.LastDailyLoginDate == today.AddDays(-1))
            {
                profile.CurrentStreakDays++;
            }
            else
            {
                profile.CurrentStreakDays = 1;
            }

            profile.LongestStreakDays = Math.Max(profile.LongestStreakDays, profile.CurrentStreakDays);
            profile.LastDailyLoginDate = today;
            profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await AwardXpAsync(
                userId,
                GamificationConstants.DailyLogin,
                GamificationConstants.DailyLoginXp,
                "DailyLogin",
                today.ToString("yyyy-MM-dd"),
                "Dnevni obisk");
        }

        public async Task<GamificationLevelInfo> GetLevelInfoAsync(string userId)
        {
            var profile = await EnsureProfileAsync(userId);
            return CalculateLevelInfo(profile.TotalXp);
        }

        public GamificationLevelInfo CalculateLevelInfo(int totalXp)
        {
            var safeXp = Math.Max(0, totalXp);
            var level = Math.Max(1, (int)Math.Floor(Math.Sqrt(safeXp / 100d)) + 1);
            return new GamificationLevelInfo
            {
                TotalXp = safeXp,
                Level = level,
                Title = GetTitle(level),
                CurrentLevelXp = RequiredXpForLevel(level),
                NextLevelXp = RequiredXpForLevel(level + 1)
            };
        }

        private static int RequiredXpForLevel(int level)
        {
            var safeLevel = Math.Max(1, level);
            return (safeLevel - 1) * (safeLevel - 1) * 100;
        }

        private static string GetTitle(int level)
        {
            if (level >= 50) return "DoggyDrop Legend";
            if (level >= 30) return "Alpha Dog";
            if (level >= 20) return "Trail Explorer";
            if (level >= 10) return "Bin Hunter";
            if (level >= 5) return "Walker";
            return "Puppy";
        }
    }
}
