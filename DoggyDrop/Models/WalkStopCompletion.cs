using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkStopCompletion
    {
        public int Id { get; set; }

        public int WalkId { get; set; }

        [ForeignKey(nameof(WalkId))]
        public Walk? Walk { get; set; }

        public int PlannedWalkStopId { get; set; }

        [ForeignKey(nameof(PlannedWalkStopId))]
        public PlannedWalkStop? PlannedWalkStop { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}
