using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkReaction
    {
        public int Id { get; set; }

        public int WalkId { get; set; }

        [ForeignKey(nameof(WalkId))]
        public Walk? Walk { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [MaxLength(30)]
        public string ReactionType { get; set; } = "paw";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
