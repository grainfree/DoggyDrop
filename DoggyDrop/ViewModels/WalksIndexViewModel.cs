using DoggyDrop.Models;

namespace DoggyDrop.ViewModels
{
    public class WalksIndexViewModel
    {
        public List<Dog> Dogs { get; set; } = new();

        public Walk? ActiveWalk { get; set; }

        public List<Walk> RecentWalks { get; set; } = new();

        public double TotalDistanceKm { get; set; }

        public int WalksThisWeek { get; set; }

        public int CompletedWalkCount { get; set; }

        public int? SelectedDogId { get; set; }

        public double AverageDistanceKm { get; set; }

        public double LongestWalkKm { get; set; }

        public int UsedBinsCount { get; set; }

        public IReadOnlyList<WeeklyWalkStat> WeeklyStats { get; set; } = [];

        public IReadOnlyList<WalkSuggestionItem> Suggestions { get; set; } = [];

        public ActivityInsightsViewModel ActivityInsights { get; set; } = new();

        public IReadOnlyList<PlannedWalkSummaryItem> RecentPlans { get; set; } = [];

        public IReadOnlyList<AchievementItem> Achievements { get; set; } = [];

        public GamificationSummaryViewModel Gamification { get; set; } = new();

        public IReadOnlyList<QuickWalkTemplate> QuickTemplates { get; set; } = [];
    }

    public class WeeklyWalkStat
    {
        public string DayLabel { get; set; } = string.Empty;

        public int WalkCount { get; set; }

        public double DistanceKm { get; set; }

        public int IntensityPercent { get; set; }
    }

    public class WalkSuggestionItem
    {
        public string Title { get; set; } = string.Empty;

        public string Area { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public double DistanceKm { get; set; }

        public string Difficulty { get; set; } = string.Empty;

        public string BestFor { get; set; } = string.Empty;
    }

    public class GamificationSummaryViewModel
    {
        public int CurrentDailyStreak { get; set; }

        public int LongestDailyStreak { get; set; }

        public int ActiveWeeksLastEight { get; set; }

        public IReadOnlyList<LeaderboardEntry> WeeklyDistanceLeaders { get; set; } = [];

        public IReadOnlyList<LeaderboardEntry> MostActiveDogs { get; set; } = [];

        public IReadOnlyList<LeaderboardEntry> TopContributors { get; set; } = [];
    }

    public class LeaderboardEntry
    {
        public string Name { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public string ValueText { get; set; } = string.Empty;
    }

    public class QuickWalkTemplate
    {
        public string Title { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;

        public string WalkStyle { get; set; } = string.Empty;

        public double DistanceKm { get; set; }

        public bool IncludeBins { get; set; }

        public bool IncludePark { get; set; }

        public bool IncludeWater { get; set; }

        public bool IncludeDogFriendly { get; set; }
    }
}
