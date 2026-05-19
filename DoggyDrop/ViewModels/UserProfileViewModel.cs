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
