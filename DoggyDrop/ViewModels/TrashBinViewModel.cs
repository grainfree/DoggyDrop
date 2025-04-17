using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinViewModel
    {
        [Required(ErrorMessage = "Ime koša je obvezno.")]
        [Display(Name = "Ime")]
        public string? Name { get; set; }

        [Required]
        [Display(Name = "Zemljepisna širina")]
        public double Latitude { get; set; }

        [Required]
        [Display(Name = "Zemljepisna dolžina")]
        public double Longitude { get; set; }

        [Display(Name = "Fotografija")]
        public IFormFile? ImageFile { get; set; }
    }
}
