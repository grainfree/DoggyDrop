using DoggyDrop.Models;

namespace DoggyDrop.ViewModels
{
    public class DogDetailsViewModel
    {
        public required Dog Dog { get; set; }

        public IReadOnlyList<Walk> RecentWalks { get; set; } = [];

        public double TotalDistanceKm { get; set; }

        public int CompletedWalkCount { get; set; }

        public int WalksThisWeek { get; set; }

        public TimeSpan TotalDuration { get; set; }

        public int EstimatedCalories { get; set; }

        public IReadOnlyList<AchievementItem> Achievements { get; set; } = [];

        public ActivityInsightsViewModel ActivityInsights { get; set; } = new();

        public IReadOnlyList<FavoriteParkItem> FavoriteParks { get; set; } = [];

        public int ParkVisitCount { get; set; }
    }

    public class FavoriteParkItem
    {
        public string ParkName { get; set; } = string.Empty;

        public string? Area { get; set; }

        public int VisitCount { get; set; }

        public DateTime LastVisitedAt { get; set; }
    }
}
