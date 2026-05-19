namespace DoggyDrop.Models
{
    public class UserGamificationProfile
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public int TotalXp { get; set; }

        public int Level { get; set; } = 1;

        public string Title { get; set; } = "Puppy";

        public int CurrentStreakDays { get; set; }

        public int LongestStreakDays { get; set; }

        public DateOnly? LastDailyLoginDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
