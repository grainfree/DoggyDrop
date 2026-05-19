using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkPhotoReaction
    {
        public int Id { get; set; }

        public int WalkPhotoId { get; set; }

        [ForeignKey(nameof(WalkPhotoId))]
        public WalkPhoto? WalkPhoto { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [MaxLength(30)]
        public string ReactionType { get; set; } = "heart";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
