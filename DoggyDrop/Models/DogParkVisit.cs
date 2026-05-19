using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class DogParkVisit
    {
        public int Id { get; set; }

        public int DogId { get; set; }

        [ForeignKey(nameof(DogId))]
        public Dog? Dog { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [Required]
        [MaxLength(120)]
        public string ParkName { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? Area { get; set; }

        [MaxLength(160)]
        public string? Address { get; set; }

        [Required]
        [MaxLength(120)]
        public string PlaceKey { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public DateTime VisitedAt { get; set; } = DateTime.UtcNow;
    }
}
