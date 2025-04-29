using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinEditViewModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        public double Latitude { get; set; }

        [Required]
        public double Longitude { get; set; }

        public string? CurrentImageUrl { get; set; } // 👉 obstoječa slika za prikaz

        public IFormFile? ImageFile { get; set; } // 👉 nova slika, če jo uporabnik želi zamenjati
    }
}
