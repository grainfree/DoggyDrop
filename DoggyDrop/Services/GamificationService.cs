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
            await RecordStreakActivityAsync(userId, GamificationStreakConstants.Daily, today);

            return await AwardXpAsync(
                userId,
                GamificationConstants.DailyLogin,
                GamificationConstants.DailyLoginXp,
                "DailyLogin",
                today.ToString("yyyy-MM-dd"),
                "Dnevni obisk");
        }

        public async Task<UserStreak?> RecordStreakActivityAsync(string? userId, string streakType, DateOnly? activityDate = null)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(streakType))
            {
                return null;
            }

            var normalizedType = streakType.Trim();
            var date = activityDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var streak = await _context.UserStreaks
                .FirstOrDefaultAsync(item => item.UserId == userId && item.StreakType == normalizedType);

            if (streak == null)
            {
                streak = new UserStreak
                {
                    UserId = userId,
                    StreakType = normalizedType,
                    CreatedAt = DateTime.UtcNow
                };
                _context.UserStreaks.Add(streak);
            }

            if (streak.LastActivityDate == date)
            {
                return streak;
            }

            if (streak.LastActivityDate == date.AddDays(-1))
            {
                streak.CurrentDays++;
            }
            else
            {
                streak.CurrentDays = 1;
            }

            streak.LongestDays = Math.Max(streak.LongestDays, streak.CurrentDays);
            streak.LastActivityDate = date;
            streak.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await NotifyStreakMilestonesAsync(userId, streak);
            return streak;
        }

        public async Task<IReadOnlyList<GamificationStreakInfo>> GetStreaksAsync(string userId)
        {
            var streaks = await _context.UserStreaks
                .Where(item => item.UserId == userId)
                .ToListAsync();

            return new[]
                {
                    BuildStreakInfo(streaks, GamificationStreakConstants.Walk, "Walk streak"),
                    BuildStreakInfo(streaks, GamificationStreakConstants.Contribution, "Contribution streak"),
                    BuildStreakInfo(streaks, GamificationStreakConstants.Explorer, "Explorer streak")
                }
                .ToList();
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

        private async Task NotifyStreakMilestonesAsync(string userId, UserStreak streak)
        {
            if (streak.CurrentDays is not (7 or 30 or 100))
            {
                return;
            }

            await _notificationService.CreateUniqueRecentAsync(
                userId,
                $"Streak:{streak.StreakType}:{streak.CurrentDays}",
                $"{GetStreakLabel(streak.StreakType)}: {streak.CurrentDays} dni",
                $"Ohranil si {GetStreakLabel(streak.StreakType).ToLowerInvariant()} {streak.CurrentDays} dni zapored.",
                "/Home/UserProfile",
                withinHours: 24 * 365);
        }

        private static GamificationStreakInfo BuildStreakInfo(IEnumerable<UserStreak> streaks, string streakType, string label)
        {
            var streak = streaks.FirstOrDefault(item => item.StreakType == streakType);
            return new GamificationStreakInfo
            {
                StreakType = streakType,
                Label = label,
                CurrentDays = streak?.CurrentDays ?? 0,
                LongestDays = streak?.LongestDays ?? 0,
                FreezeCredits = streak?.FreezeCredits ?? 0,
                LastActivityDate = streak?.LastActivityDate
            };
        }

        private static string GetStreakLabel(string streakType)
        {
            return streakType switch
            {
                GamificationStreakConstants.Walk => "Walk streak",
                GamificationStreakConstants.Contribution => "Contribution streak",
                GamificationStreakConstants.Explorer => "Explorer streak",
                GamificationStreakConstants.Daily => "Daily streak",
                _ => "Streak"
            };
        }
    }
}
