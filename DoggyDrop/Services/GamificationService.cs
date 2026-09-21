using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services
{
    public class GamificationService : IGamificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IGamificationCalendar _calendar;

        public GamificationService(ApplicationDbContext context, INotificationService notificationService, IGamificationCalendar calendar)
        {
            _context = context;
            _notificationService = notificationService;
            _calendar = calendar;
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

            var capturedNow = _calendar.UtcNow;
            var today = _calendar.ToLocalDate(capturedNow.UtcDateTime);
            var utcDate = DateOnly.FromDateTime(capturedNow.UtcDateTime);
            var profile = await EnsureProfileAsync(userId);
            if (profile.LastDailyLoginDate == today)
            {
                return null;
            }

            // During the UTC-to-Ljubljana rollout, the legacy UTC reference can
            // represent this same local day. OccurredAt distinguishes it from a
            // legitimate reward earned on the previous Ljubljana day.
            if (utcDate != today)
            {
                var legacyCandidates = await _context.UserXpEvents.AsNoTracking()
                    .Where(item => item.UserId == userId
                        && item.ActivityType == GamificationConstants.DailyLogin
                        && item.ReferenceType == "DailyLogin"
                        && item.ReferenceId == utcDate.ToString("yyyy-MM-dd"))
                    .ToListAsync();
                if (legacyCandidates.Any(item => _calendar.ToLocalDate(item.OccurredAt) == today))
                {
                    profile.LastDailyLoginDate = today;
                    profile.UpdatedAt = capturedNow.UtcDateTime;
                    await _context.SaveChangesAsync();
                    return null;
                }
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
            profile.UpdatedAt = capturedNow.UtcDateTime;
            await _context.SaveChangesAsync();
            await RecordStreakActivityAtAsync(userId, GamificationStreakConstants.Daily, capturedNow.UtcDateTime);

            return await AwardXpAsync(
                userId,
                GamificationConstants.DailyLogin,
                GamificationConstants.DailyLoginXp,
                "DailyLogin",
                today.ToString("yyyy-MM-dd"),
                "Dnevni obisk");
        }

        public Task<UserStreak?> RecordStreakActivityAsync(string? userId, string streakType) =>
            RecordStreakActivityCoreAsync(userId, streakType, _calendar.Today);

        public Task<UserStreak?> RecordStreakActivityAtAsync(string? userId, string streakType, DateTime utcEventInstant)
        {
            if (utcEventInstant.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("The event instant must be UTC.", nameof(utcEventInstant));
            }

            var eventDate = _calendar.ToLocalDate(utcEventInstant);
            var today = _calendar.Today;
            if (eventDate > today || eventDate < today.AddDays(-1))
            {
                throw new ArgumentOutOfRangeException(nameof(utcEventInstant), "Streak events must belong to today or yesterday in Europe/Ljubljana.");
            }

            return RecordStreakActivityCoreAsync(userId, streakType, eventDate);
        }

        private async Task<UserStreak?> RecordStreakActivityCoreAsync(string? userId, string streakType, DateOnly activityDate)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(streakType))
            {
                return null;
            }

            var normalizedType = streakType.Trim();
            var date = activityDate;
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

            if (normalizedType != GamificationStreakConstants.Daily)
            {
                await NotifyStreakMilestonesAsync(userId, streak);
            }
            return streak;
        }

        public async Task<IReadOnlyList<GamificationStreakInfo>> GetStreaksAsync(string userId)
        {
            var streaks = await _context.UserStreaks
                .Where(item => item.UserId == userId)
                .ToListAsync();

            return new[]
                {
                    BuildStreakInfo(streaks, GamificationStreakConstants.Walk),
                    BuildStreakInfo(streaks, GamificationStreakConstants.Contribution),
                    BuildStreakInfo(streaks, GamificationStreakConstants.Explorer)
                }
                .ToList();
        }

        public async Task<GamificationStreakInfo> GetStreakAsync(string userId, string streakType)
        {
            var streak = await _context.UserStreaks.AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId && item.StreakType == streakType);
            return GetEffectiveStreak(streak, streakType);
        }

        public GamificationStreakInfo GetEffectiveStreak(UserStreak? streak, string streakType)
        {
            var today = _calendar.Today;
            var storedDays = Math.Max(0, streak?.CurrentDays ?? 0);
            var hasValidPositiveStreak = storedDays > 0;
            var state = streak?.LastActivityDate switch
            {
                var date when hasValidPositiveStreak && date == today => GamificationStreakState.SafeToday,
                var date when hasValidPositiveStreak && date == today.AddDays(-1) => GamificationStreakState.AtRiskToday,
                _ => GamificationStreakState.Expired
            };
            var effectiveDays = state == GamificationStreakState.Expired ? 0 : storedDays;

            return new GamificationStreakInfo
            {
                StreakType = streakType,
                Label = GetStreakLabel(streakType),
                StoredCurrentDays = storedDays,
                EffectiveCurrentDays = effectiveDays,
                LongestDays = Math.Max(Math.Max(0, streak?.LongestDays ?? 0), effectiveDays),
                FreezeCredits = streak?.FreezeCredits ?? 0,
                LastActivityDate = streak?.LastActivityDate,
                State = state
            };
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

        private GamificationStreakInfo BuildStreakInfo(IEnumerable<UserStreak> streaks, string streakType)
        {
            var streak = streaks.FirstOrDefault(item => item.StreakType == streakType);
            return GetEffectiveStreak(streak, streakType);
        }

        private static string GetStreakLabel(string streakType)
        {
            return streakType switch
            {
                GamificationStreakConstants.Walk => "Sprehajalni niz",
                GamificationStreakConstants.Contribution => "Prispevni niz",
                GamificationStreakConstants.Explorer => "Raziskovalni niz",
                GamificationStreakConstants.Daily => "Dnevni obisk",
                _ => "Niz"
            };
        }
    }
}
