namespace DoggyDrop.Models
{
    public class DogProgressionProfile
    {
        public int Id { get; set; }

        public int DogId { get; set; }

        public Dog? Dog { get; set; }

        public int TotalXp { get; set; }

        public int Level { get; set; } = 1;

        public string DogClass { get; set; } = "Urban Sniffer";

        public int Adventure { get; set; }

        public int Social { get; set; }

        public int Forest { get; set; }

        public int City { get; set; }

        public int Water { get; set; }

        public int Speed { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
