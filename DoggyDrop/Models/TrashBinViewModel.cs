using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DoggyDrop.Models
{
    public class TrashBinViewModel
    {
        [Required]
        [Display(Name = "Ime lokacije")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Zemljepisna širina")]
        public string Latitude { get; set; } = "";

        [Required]
        [Display(Name = "Zemljepisna dolžina")]
        public string Longitude { get; set; } = "";

        public IFormFile? ImageFile { get; set; }
    }
}
