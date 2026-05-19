namespace DoggyDrop.ViewModels
{
    public class CommunityViewModel
    {
        public IReadOnlyList<CommunityWalkItem> RecentWalks { get; set; } = [];

        public IReadOnlyList<CommunityWalkItem> FriendsWalks { get; set; } = [];

        public IReadOnlyList<CommunityLeaderboardItem> WeeklyLeaders { get; set; } = [];

        public LocalLeaderboardViewModel LocalLeaderboards { get; set; } = new();

        public IReadOnlyList<CommunityPhotoFeedItem> PhotoFeed { get; set; } = [];

        public IReadOnlyList<CommunityBinPhotoItem> BinPhotoGallery { get; set; } = [];

        public int WalksThisWeek { get; set; }

        public double KilometersThisWeek { get; set; }

        public int ActiveDogsThisWeek { get; set; }
    }

    public class LocalLeaderboardViewModel
    {
        public string CityKey { get; set; } = "maribor";

        public string CityName { get; set; } = "Maribor";

        public IReadOnlyList<LocalLeaderboardEntryViewModel> MostDistance { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntryViewModel> MostDiscoveries { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntryViewModel> MostHelpful { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntryViewModel> BestPhotos { get; set; } = [];

        public IReadOnlyList<LocalLeaderboardEntryViewModel> TopDogsThisWeek { get; set; } = [];
    }

    public class LocalLeaderboardEntryViewModel
    {
        public int Rank { get; set; }

        public string Label { get; set; } = string.Empty;

        public string? SubLabel { get; set; }

        public string? ImageUrl { get; set; }

        public string ScoreText { get; set; } = string.Empty;

        public int? DogId { get; set; }
    }

    public class CommunityWalkItem
    {
        public int WalkId { get; set; }

        public string DogName { get; set; } = string.Empty;

        public string? DogPhotoUrl { get; set; }

        public string OwnerName { get; set; } = string.Empty;

        public DateTime StartedAt { get; set; }

        public double DistanceKm { get; set; }

        public int UsedBinsCount { get; set; }

        public int LikeCount { get; set; }

        public int CommentCount { get; set; }

        public bool IsLikedByCurrentUser { get; set; }

        public string? CoverPhotoUrl { get; set; }

        public int PhotoCount { get; set; }
    }

    public class CommunityLeaderboardItem
    {
        public int DogId { get; set; }

        public string DogName { get; set; } = string.Empty;

        public string? DogPhotoUrl { get; set; }

        public string OwnerName { get; set; } = string.Empty;

        public double WeeklyDistanceKm { get; set; }

        public int WalkCount { get; set; }
    }

    public class CommunityPhotoFeedItem
    {
        public int WalkId { get; set; }

        public int PhotoId { get; set; }

        public string ImageUrl { get; set; } = string.Empty;

        public string? Caption { get; set; }

        public string DogName { get; set; } = string.Empty;

        public string? DogPhotoUrl { get; set; }

        public string OwnerName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public int ReactionCount { get; set; }

        public bool IsReactedByCurrentUser { get; set; }

        public string? StopName { get; set; }
    }

    public class CommunityBinPhotoItem
    {
        public int BinId { get; set; }

        public string BinName { get; set; } = string.Empty;

        public string ImageUrl { get; set; } = string.Empty;

        public string? ContributorName { get; set; }

        public DateTime DateAdded { get; set; }
    }
}
