using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class PlannedWalk
    {
        public int Id { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey(nameof(OwnerId))]
        public ApplicationUser? Owner { get; set; }

        public int? DogId { get; set; }

        [ForeignKey(nameof(DogId))]
        public Dog? Dog { get; set; }

        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(40)]
        public string AreaKey { get; set; } = string.Empty;

        [MaxLength(80)]
        public string AreaName { get; set; } = string.Empty;

        public double TargetDistanceKm { get; set; }

        public double EstimatedDistanceKm { get; set; }

        public int EstimatedMinutes { get; set; }

        public bool IncludeBins { get; set; }

        public bool IncludePark { get; set; }

        public bool IncludeWater { get; set; }

        public bool IncludeDogFriendly { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UsedAt { get; set; }

        public ICollection<PlannedWalkStop>? Stops { get; set; }

        public ICollection<PlannedWalkRoutePoint>? RoutePoints { get; set; }
    }
}
