using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinEditViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ime koša je obvezno.")]
        [StringLength(100, ErrorMessage = "Ime ne sme presegati 100 znakov.")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Zahtevana je širina (Latitude).")]
        public double Latitude { get; set; }

        [Required(ErrorMessage = "Zahtevana je dolžina (Longitude).")]
        public double Longitude { get; set; }

        // 📷 Obstoječa slika, prikazana v obrazcu
        public string? CurrentImageUrl { get; set; }

        // 📤 Nova slika (če želi zamenjati)
        public IFormFile? ImageFile { get; set; }
    }
}
