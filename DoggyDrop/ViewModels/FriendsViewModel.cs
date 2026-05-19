namespace DoggyDrop.ViewModels
{
    public class FriendsViewModel
    {
        public IReadOnlyList<FriendItem> Friends { get; set; } = [];

        public IReadOnlyList<FriendRequestItem> IncomingRequests { get; set; } = [];

        public IReadOnlyList<FriendRequestItem> OutgoingRequests { get; set; } = [];

        public IReadOnlyList<FriendSuggestionItem> Suggestions { get; set; } = [];

        public int FriendCount { get; set; }

        public int IncomingCount { get; set; }
    }

    public class FriendItem
    {
        public int FriendshipId { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }

        public int DogCount { get; set; }

        public double WeeklyDistanceKm { get; set; }

        public DateTime FriendsSince { get; set; }
    }

    public class FriendRequestItem
    {
        public int FriendshipId { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    public class FriendSuggestionItem
    {
        public string UserId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? PhotoUrl { get; set; }

        public int DogCount { get; set; }

        public double WeeklyDistanceKm { get; set; }
    }
}
