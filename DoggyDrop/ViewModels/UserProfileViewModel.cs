namespace DoggyDrop.ViewModels
{
    public class UserProfileViewModel
    {
        public string Email { get; set; } = string.Empty;

        public int TotalBins { get; set; }

        public List<string> Badges { get; set; } = new();

        public IReadOnlyList<AchievementItem> Achievements { get; set; } = [];

        public string? DisplayName { get; set; }

        public string? ProfileImageUrl { get; set; }

        public int TotalDogs { get; set; }

        public int TotalWalks { get; set; }

        public int WalksThisWeek { get; set; }

        public double TotalDistanceKm { get; set; }

        public TimeSpan TotalWalkDuration { get; set; }

        public IReadOnlyList<ProfileDogSummary> Dogs { get; set; } = [];

        public ActivityInsightsViewModel ActivityInsights { get; set; } = new();

        public GamificationProfileViewModel Gamification { get; set; } = new();

        public IReadOnlyList<SeasonalEventViewModel> SeasonalEvents { get; set; } = [];

        public string SeasonalMapTheme { get; set; } = "default";

        public MapStampCollectionViewModel MapStamps { get; set; } = new();

        public IReadOnlyList<FounderBadgeViewModel> FounderBadges { get; set; } = [];
    }

    public class FounderBadgeViewModel
    {
        public string AreaName { get; set; } = string.Empty;

        public string BadgeType { get; set; } = string.Empty;

        public DateTime UnlockedAt { get; set; }
    }

    public class MapStampCollectionViewModel
    {
        public int TotalStamps { get; set; }

        public int CommonCount { get; set; }

        public int RareCount { get; set; }

        public int EpicCount { get; set; }

        public int LegendaryCount { get; set; }

        public IReadOnlyList<MapStampViewModel> Stamps { get; set; } = [];
    }

    public class MapStampViewModel
    {
        public string Name { get; set; } = string.Empty;

        public string? Area { get; set; }

        public string Rarity { get; set; } = "Common";

        public int VisitCount { get; set; }

        public DateTime LastCollectedAt { get; set; }
    }

    public class SeasonalEventViewModel
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string RewardName { get; set; } = string.Empty;

        public string Theme { get; set; } = string.Empty;

        public DateOnly EndsOn { get; set; }

        public int Current { get; set; }

        public int Target { get; set; }

        public int ProgressPercent { get; set; }

        public bool IsComplete { get; set; }
    }

    public class GamificationProfileViewModel
    {
        public int TotalXp { get; set; }

        public int Level { get; set; } = 1;

        public string Title { get; set; } = "Puppy";

        public int ProgressPercent { get; set; }

        public int XpIntoLevel { get; set; }

        public int XpForNextLevel { get; set; } = 1;

        public int XpRemaining { get; set; }

        public int CurrentStreakDays { get; set; }

        public int LongestStreakDays { get; set; }

        public IReadOnlyList<GamificationStreakViewModel> Streaks { get; set; } = [];

        public string AvatarFlameTier { get; set; } = "none";
    }

    public class GamificationStreakViewModel
    {
        public string StreakType { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int CurrentDays { get; set; }

        public int LongestDays { get; set; }

        public int FreezeCredits { get; set; }

        public string FlameTier { get; set; } = "none";
    }

    public class ProfileDogSummary
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }

        public double DistanceKm { get; set; }

        public int WalkCount { get; set; }
    }
}
