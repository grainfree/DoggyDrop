using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class PlaydateRequest
    {
        public int Id { get; set; }

        [Required]
        public int DogId { get; set; }

        [ForeignKey(nameof(DogId))]
        public Dog? Dog { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey(nameof(OwnerId))]
        public ApplicationUser? Owner { get; set; }

        [MaxLength(80)]
        public string LocationLabel { get; set; } = string.Empty;

        public DateTime PreferredAt { get; set; } = DateTime.UtcNow.AddDays(1);

        [MaxLength(40)]
        public string SizePreference { get; set; } = "Vse velikosti";

        [MaxLength(40)]
        public string EnergyLevel { get; set; } = "Srednja";

        [MaxLength(240)]
        public string? Note { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Open";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PlaydateInterest>? Interests { get; set; }
    }
}
