using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class DogEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ime psa je obvezno.")]
        [StringLength(80, ErrorMessage = "Ime je lahko dolgo najvec 80 znakov.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(80)]
        public string? Breed { get; set; }

        [Range(0, 30, ErrorMessage = "Starost mora biti med 0 in 30 let.")]
        public int? AgeYears { get; set; }

        [StringLength(30)]
        public string? Gender { get; set; }

        [StringLength(30)]
        public string? Size { get; set; }

        [StringLength(240, ErrorMessage = "Opis karakterja je lahko dolg najvec 240 znakov.")]
        public string? Character { get; set; }

        [StringLength(30)]
        public string NearbyVisibility { get; set; } = "Invisible";

        public string? ApproximateLocation { get; set; }

        public string? CurrentPhotoUrl { get; set; }

        public IFormFile? Photo { get; set; }
    }
}
