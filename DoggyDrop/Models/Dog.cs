using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class Dog
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(80)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? Breed { get; set; }

        [Range(0, 30)]
        public int? AgeYears { get; set; }

        [MaxLength(30)]
        public string? Gender { get; set; }

        [MaxLength(30)]
        public string? Size { get; set; }

        [MaxLength(240)]
        public string? Character { get; set; }

        public string? PhotoUrl { get; set; }

        [MaxLength(30)]
        public string NearbyVisibility { get; set; } = "Invisible";

        public double? LastKnownLatitude { get; set; }

        public double? LastKnownLongitude { get; set; }

        public DateTime? LastLocationUpdatedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey("OwnerId")]
        public ApplicationUser? Owner { get; set; }

        public ICollection<Walk>? Walks { get; set; }

        public ICollection<PlaydateRequest>? PlaydateRequests { get; set; }

        public ICollection<PlaydateInterest>? PlaydateInterests { get; set; }

        public ICollection<DogParkVisit>? ParkVisits { get; set; }

        public ICollection<PlannedWalk>? PlannedWalks { get; set; }
    }
}
