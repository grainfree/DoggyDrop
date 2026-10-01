using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinEditViewModel
    {
        public int Id { get; set; }
        public string? Snapshot { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = default!;

        [Required]
        [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))]
        public double Latitude { get; set; }

        [Required]
        [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))]
        public double Longitude { get; set; }

        public string? CurrentImageUrl { get; set; }

        public IFormFile? ImageFile { get; set; }
    }
}
