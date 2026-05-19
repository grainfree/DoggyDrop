using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class Walk
    {
        public int Id { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public DateTime? EndedAt { get; set; }

        public double DistanceMeters { get; set; }

        public int UsedBinsCount { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Active";

        public int DogId { get; set; }

        [ForeignKey("DogId")]
        public Dog? Dog { get; set; }

        public int? PlannedWalkId { get; set; }

        [ForeignKey(nameof(PlannedWalkId))]
        public PlannedWalk? PlannedWalk { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey("OwnerId")]
        public ApplicationUser? Owner { get; set; }

        public ICollection<WalkPoint>? Points { get; set; }

        public ICollection<WalkReaction>? Reactions { get; set; }

        public ICollection<WalkComment>? Comments { get; set; }

        public ICollection<WalkStopCompletion>? StopCompletions { get; set; }

        public ICollection<WalkPhoto>? Photos { get; set; }
    }
}
