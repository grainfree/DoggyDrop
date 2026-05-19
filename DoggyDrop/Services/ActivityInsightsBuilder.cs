using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services
{
    public static class ActivityInsightsBuilder
    {
        public static ActivityInsightsViewModel Build(IReadOnlyList<Walk> completedWalks, double weeklyGoalKm = 10, double monthlyGoalKm = 40)
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-6);
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var totalDistanceKm = completedWalks.Sum(w => w.DistanceMeters) / 1000;
            var weeklyDistanceKm = completedWalks
                .Where(w => w.StartedAt.Date >= weekStart)
                .Sum(w => w.DistanceMeters) / 1000;
            var monthlyDistanceKm = completedWalks
                .Where(w => w.StartedAt.Date >= monthStart)
                .Sum(w => w.DistanceMeters) / 1000;
            var walkedDates = completedWalks
                .Select(w => w.StartedAt.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            var streak = CalculateCurrentStreak(walkedDates, today);
            var longestStreak = CalculateLongestStreak(walkedDates);
            var weeklyWalks = completedWalks.Count(w => w.StartedAt.Date >= weekStart);
            var monthlyWalks = completedWalks.Count(w => w.StartedAt.Date >= monthStart);
            var usedBinsThisMonth = completedWalks
                .Where(w => w.StartedAt.Date >= monthStart)
                .Sum(w => w.UsedBinsCount);

            return new ActivityInsightsViewModel
            {
                CurrentStreakDays = streak,
                LongestStreakDays = longestStreak,
                WeeklyDistanceKm = weeklyDistanceKm,
                WeeklyGoalKm = weeklyGoalKm,
                WeeklyGoalPercent = GetProgressPercent(weeklyDistanceKm, weeklyGoalKm),
                MonthlyDistanceKm = monthlyDistanceKm,
                MonthlyGoalKm = monthlyGoalKm,
                MonthlyGoalPercent = GetProgressPercent(monthlyDistanceKm, monthlyGoalKm),
                NextMilestoneName = GetNextMilestoneName(totalDistanceKm),
                NextMilestoneProgress = GetNextMilestoneProgress(totalDistanceKm),
                NextMilestonePercent = GetNextMilestonePercent(totalDistanceKm),
                Challenges =
                [
                    BuildChallenge("Tedenski ritem", "Zakljuci 3 sprehode v zadnjih 7 dneh.", weeklyWalks, 3, "sprehodov"),
                    BuildChallenge("10 km teden", "Ta teden skupaj prehodi 10 km.", weeklyDistanceKm, 10, "km"),
                    BuildChallenge("Mesec aktivnosti", "Ta mesec zakljuci 12 sprehodov.", monthlyWalks, 12, "sprehodov"),
                    BuildChallenge("Cist mestni krog", "Ta mesec uporabi 5 pasjih kosev.", usedBinsThisMonth, 5, "uporab")
                ]
            };
        }

        private static int CalculateCurrentStreak(IReadOnlyList<DateTime> walkedDates, DateTime today)
        {
            if (walkedDates.Count == 0)
            {
                return 0;
            }

            var dateSet = walkedDates.ToHashSet();
            var cursor = dateSet.Contains(today) ? today : today.AddDays(-1);
            var streak = 0;

            while (dateSet.Contains(cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        private static int CalculateLongestStreak(IReadOnlyList<DateTime> walkedDates)
        {
            var longest = 0;
            var current = 0;
            DateTime? previous = null;

            foreach (var date in walkedDates)
            {
                current = previous.HasValue && date == previous.Value.AddDays(1)
                    ? current + 1
                    : 1;
                longest = Math.Max(longest, current);
                previous = date;
            }

            return longest;
        }

        private static ActivityChallengeItem BuildChallenge(string title, string description, double current, double target, string suffix)
        {
            var currentText = suffix == "km" ? current.ToString("0.0") : Math.Floor(current).ToString("0");
            var targetText = suffix == "km" ? target.ToString("0") : target.ToString("0");

            return new ActivityChallengeItem
            {
                Title = title,
                Description = description,
                ProgressPercent = GetProgressPercent(current, target),
                ProgressText = $"{currentText} / {targetText} {suffix}",
                IsComplete = current >= target
            };
        }

        private static string GetNextMilestoneName(double totalDistanceKm)
        {
            var target = GetNextMilestoneTarget(totalDistanceKm);
            return target >= 100 ? "Trail master" : $"{target:0} km club";
        }

        private static string GetNextMilestoneProgress(double totalDistanceKm)
        {
            var target = GetNextMilestoneTarget(totalDistanceKm);
            return $"{totalDistanceKm:0.0} / {target:0} km";
        }

        private static int GetNextMilestonePercent(double totalDistanceKm)
        {
            return GetProgressPercent(totalDistanceKm, GetNextMilestoneTarget(totalDistanceKm));
        }

        private static double GetNextMilestoneTarget(double totalDistanceKm)
        {
            if (totalDistanceKm < 10)
            {
                return 10;
            }

            if (totalDistanceKm < 25)
            {
                return 25;
            }

            if (totalDistanceKm < 50)
            {
                return 50;
            }

            if (totalDistanceKm < 100)
            {
                return 100;
            }

            return Math.Ceiling((totalDistanceKm + 1) / 100) * 100;
        }

        private static int GetProgressPercent(double current, double target)
        {
            return (int)Math.Min(100, Math.Round(current / Math.Max(target, 1) * 100));
        }
    }
}
