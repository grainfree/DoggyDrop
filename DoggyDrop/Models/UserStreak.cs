namespace DoggyDrop.Models
{
    public class UserStreak
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public string StreakType { get; set; } = string.Empty;

        public int CurrentDays { get; set; }

        public int LongestDays { get; set; }

        public int FreezeCredits { get; set; }

        public DateOnly? LastActivityDate { get; set; }

        public DateOnly? LastFreezeUsedDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
