using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkPhoto
    {
        public int Id { get; set; }

        public int WalkId { get; set; }

        [ForeignKey(nameof(WalkId))]
        public Walk? Walk { get; set; }

        [Required]
        [MaxLength(260)]
        public string ImageUrl { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? Caption { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        public int? PlannedWalkStopId { get; set; }

        [ForeignKey(nameof(PlannedWalkStopId))]
        public PlannedWalkStop? PlannedWalkStop { get; set; }

        public ICollection<WalkPhotoReaction>? Reactions { get; set; }
    }
}
