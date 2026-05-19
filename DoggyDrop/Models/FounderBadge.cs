using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models
{
    public class FounderBadge
    {
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        public ApplicationUser? User { get; set; }

        [MaxLength(80)]
        public string AreaKey { get; set; } = string.Empty;

        [MaxLength(120)]
        public string AreaName { get; set; } = string.Empty;

        [MaxLength(40)]
        public string BadgeType { get; set; } = "ExplorerFounder";

        public int? TrashBinId { get; set; }

        public TrashBin? TrashBin { get; set; }

        public DateTime UnlockedAt { get; set; } = DateTime.UtcNow;
    }
}
