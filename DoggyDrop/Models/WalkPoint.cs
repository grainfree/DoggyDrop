using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkPoint
    {
        public int Id { get; set; }

        public int WalkId { get; set; }

        [ForeignKey("WalkId")]
        public Walk? Walk { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    }
}
