using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class PlaydateInterest
    {
        public int Id { get; set; }

        public int PlaydateRequestId { get; set; }

        [ForeignKey(nameof(PlaydateRequestId))]
        public PlaydateRequest? PlaydateRequest { get; set; }

        public int DogId { get; set; }

        [ForeignKey(nameof(DogId))]
        public Dog? Dog { get; set; }

        [Required]
        public string OwnerId { get; set; } = string.Empty;

        [ForeignKey(nameof(OwnerId))]
        public ApplicationUser? Owner { get; set; }

        [MaxLength(180)]
        public string? Message { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Interested";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
