namespace DoggyDrop.ViewModels
{
    public class ActivityInsightsViewModel
    {
        public int CurrentStreakDays { get; set; }

        public int LongestStreakDays { get; set; }

        public double WeeklyDistanceKm { get; set; }

        public double WeeklyGoalKm { get; set; } = 10;

        public int WeeklyGoalPercent { get; set; }

        public double MonthlyDistanceKm { get; set; }

        public double MonthlyGoalKm { get; set; } = 40;

        public int MonthlyGoalPercent { get; set; }

        public string NextMilestoneName { get; set; } = string.Empty;

        public string NextMilestoneProgress { get; set; } = string.Empty;

        public int NextMilestonePercent { get; set; }

        public IReadOnlyList<ActivityChallengeItem> Challenges { get; set; } = [];
    }

    public class ActivityChallengeItem
    {
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int ProgressPercent { get; set; }

        public string ProgressText { get; set; } = string.Empty;

        public bool IsComplete { get; set; }
    }
}
