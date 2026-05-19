using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;

namespace DoggyDrop.ViewModels
{
    public class PlaydatesViewModel
    {
        public IReadOnlyList<PlaydateRequest> OpenRequests { get; set; } = [];

        public IReadOnlyList<Dog> MyDogs { get; set; } = [];

        public PlaydateCreateViewModel Create { get; set; } = new();

        public PlaydateInterestViewModel Interest { get; set; } = new();
    }

    public class PlaydateCreateViewModel
    {
        [Required(ErrorMessage = "Izberi psa.")]
        public int DogId { get; set; }

        [Required(ErrorMessage = "Lokacija je obvezna.")]
        [StringLength(80)]
        public string LocationLabel { get; set; } = string.Empty;

        [Required]
        public DateTime PreferredAt { get; set; } = DateTime.Now.AddDays(1);

        [StringLength(40)]
        public string SizePreference { get; set; } = "Vse velikosti";

        [StringLength(40)]
        public string EnergyLevel { get; set; } = "Srednja";

        [StringLength(240)]
        public string? Note { get; set; }
    }

    public class PlaydateInterestViewModel
    {
        [Required(ErrorMessage = "Izberi psa.")]
        public int DogId { get; set; }

        [StringLength(180)]
        public string? Message { get; set; }
    }
}
