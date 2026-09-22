namespace DoggyDrop.ViewModels
{
    public class GamificationRewardResultViewModel
    {
        public int WalkId { get; set; }

        public UserRewardViewModel? UserReward { get; set; }

        public DogRewardViewModel? DogReward { get; set; }

        public StreakRewardViewModel? StreakReward { get; set; }

        public IReadOnlyList<RewardAchievementViewModel> UnlockedAchievements { get; set; } = [];

        public RewardNextGoalViewModel? NextGoal { get; set; }

        public bool HasRewards =>
            UserReward != null ||
            DogReward != null ||
            StreakReward != null ||
            UnlockedAchievements.Count > 0;

        public IReadOnlyList<RewardAchievementViewModel> GetVisibleAchievements(bool showFirstWalkCelebration)
        {
            return UnlockedAchievements
                .Where(achievement => !showFirstWalkCelebration || achievement.Key != Services.UserAchievementCatalog.WalkFirst)
                .ToList();
        }
    }

    public class UserRewardViewModel
    {
        public int XpEarned { get; set; }

        public int PreviousXp { get; set; }

        public int CurrentXp { get; set; }

        public int PreviousLevel { get; set; }

        public int CurrentLevel { get; set; }

        public string LevelTitle { get; set; } = string.Empty;

        public int XpRemaining { get; set; }

        public int ProgressPercent { get; set; }

        public bool LeveledUp => CurrentLevel > PreviousLevel;
    }

    public class DogRewardViewModel
    {
        public int DogId { get; set; }

        public string DogName { get; set; } = string.Empty;

        public int XpEarned { get; set; }

        public int PreviousXp { get; set; }

        public int CurrentXp { get; set; }

        public int PreviousLevel { get; set; }

        public int CurrentLevel { get; set; }

        public string PreviousClass { get; set; } = string.Empty;

        public string CurrentClass { get; set; } = string.Empty;

        public int XpRemaining { get; set; }

        public int ProgressPercent { get; set; }

        public DogProgressionChangesViewModel ProgressionChanges { get; set; } = new();

        public bool LeveledUp => CurrentLevel > PreviousLevel;

        public bool ClassChanged => !string.Equals(PreviousClass, CurrentClass, StringComparison.Ordinal);
    }

    public class DogProgressionChangesViewModel
    {
        public int Adventure { get; set; }

        public int Social { get; set; }

        public int Forest { get; set; }

        public int City { get; set; }

        public int Water { get; set; }

        public int Speed { get; set; }

        public IReadOnlyList<string> GetVisibleChanges()
        {
            var changes = new List<string>();
            AddChange(changes, "Pustolovščina", Adventure);
            AddChange(changes, "Družabnost", Social);
            AddChange(changes, "Gozd", Forest);
            AddChange(changes, "Mesto", City);
            AddChange(changes, "Voda", Water);
            AddChange(changes, "Hitrost", Speed);
            return changes;
        }

        private static void AddChange(ICollection<string> changes, string label, int amount)
        {
            if (amount > 0)
            {
                changes.Add($"{label} +{amount}");
            }
        }
    }

    public class StreakRewardViewModel
    {
        public int PreviousDays { get; set; }

        public int CurrentDays { get; set; }

        public bool Increased { get; set; }

        public int? MilestoneReached { get; set; }

        public Services.GamificationStreakState State { get; set; }
    }

    public class RewardAchievementViewModel
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }

    public class RewardNextGoalViewModel
    {
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int ProgressPercent { get; set; }

        public string ActionUrl { get; set; } = string.Empty;

        public string ActionLabel { get; set; } = string.Empty;
    }
}
