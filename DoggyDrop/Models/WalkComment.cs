using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class WalkComment
    {
        public int Id { get; set; }

        public int WalkId { get; set; }

        [ForeignKey(nameof(WalkId))]
        public Walk? Walk { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [Required]
        [MaxLength(240)]
        public string Body { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; }
    }
}
