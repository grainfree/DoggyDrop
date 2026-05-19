namespace DoggyDrop.Services
{
    public class LocalLeaderboardBoard
    {
        public string CityKey { get; set; } = "maribor";

        public string CityName { get; set; } = "Maribor";

        public IReadOnlyList<LocalLeaderboardEntry> MostDistance { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntry> MostDiscoveries { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntry> MostHelpful { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntry> BestPhotos { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntry> TopDogsThisWeek { get; set; } = [];
    }

    public class LocalLeaderboardEntry
    {
        public int Rank { get; set; }

        public string Label { get; set; } = string.Empty;

        public string? SubLabel { get; set; }

        public string? ImageUrl { get; set; }

        public double Score { get; set; }

        public string ScoreText { get; set; } = string.Empty;

        public int? DogId { get; set; }

        public string? UserId { get; set; }
    }
}
