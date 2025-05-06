using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ime koša je obvezno.")]
        public string Name { get; set; } = default!;

        [Required(ErrorMessage = "Zahtevana je širina (Latitude).")]
        public double Latitude { get; set; }

        [Required(ErrorMessage = "Zahtevana je dolžina (Longitude).")]
        public double Longitude { get; set; }

        public IFormFile? ImageFile { get; set; }
    }
}
