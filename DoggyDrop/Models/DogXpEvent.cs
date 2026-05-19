namespace DoggyDrop.Models
{
    public class DogXpEvent
    {
        public int Id { get; set; }

        public int DogId { get; set; }

        public Dog? Dog { get; set; }

        public string ActivityType { get; set; } = string.Empty;

        public int XpAmount { get; set; }

        public string? ReferenceType { get; set; }

        public string? ReferenceId { get; set; }

        public string? Description { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
