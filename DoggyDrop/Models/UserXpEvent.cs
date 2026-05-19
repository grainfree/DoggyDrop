namespace DoggyDrop.Models
{
    public class UserXpEvent
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        public string ActivityType { get; set; } = string.Empty;

        public int XpAmount { get; set; }

        public string? ReferenceType { get; set; }

        public string? ReferenceId { get; set; }

        public string? Description { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
