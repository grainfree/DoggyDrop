using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class UserNotification
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [MaxLength(40)]
        public string Type { get; set; } = "General";

        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(280)]
        public string Body { get; set; } = string.Empty;

        [MaxLength(260)]
        public string? LinkUrl { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }
    }
}
