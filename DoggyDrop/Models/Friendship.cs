using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class Friendship
    {
        public int Id { get; set; }

        [Required]
        public string RequesterId { get; set; } = string.Empty;

        [ForeignKey(nameof(RequesterId))]
        public ApplicationUser? Requester { get; set; }

        [Required]
        public string AddresseeId { get; set; } = string.Empty;

        [ForeignKey(nameof(AddresseeId))]
        public ApplicationUser? Addressee { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? RespondedAt { get; set; }
    }
}
