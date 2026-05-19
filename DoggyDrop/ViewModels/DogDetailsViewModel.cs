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

        public DogProgressionViewModel Progression { get; set; } = new();

        public IReadOnlyList<DogMemoryItem> Memories { get; set; } = [];

        public IReadOnlyList<FavoriteParkItem> FavoriteParks { get; set; } = [];

        public int ParkVisitCount { get; set; }
    }

    public class DogProgressionViewModel
    {
        public int TotalXp { get; set; }

        public int Level { get; set; } = 1;

        public int XpRemaining { get; set; }

        public int ProgressPercent { get; set; }

        public string DogClass { get; set; } = "Urban Sniffer";

        public int Adventure { get; set; }

        public int Social { get; set; }

        public int Forest { get; set; }

        public int City { get; set; }

        public int Water { get; set; }

        public int Speed { get; set; }
    }

    public class DogMemoryItem
    {
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public string? ImageUrl { get; set; }
    }

    public class FavoriteParkItem
    {
        public string ParkName { get; set; } = string.Empty;

        public string? Area { get; set; }

        public int VisitCount { get; set; }

        public DateTime LastVisitedAt { get; set; }
    }
}
