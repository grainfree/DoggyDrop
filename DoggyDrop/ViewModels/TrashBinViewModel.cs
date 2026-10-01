using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.ViewModels
{
    public class TrashBinViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Ime koša je obvezno."), StringLength(100)]
        public string Name { get; set; } = default!;

        [Required(ErrorMessage = "Zahtevana je širina (Latitude).")]
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
        [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))]
        public double Latitude { get; set; }

        [Required(ErrorMessage = "Zahtevana je dolžina (Longitude).")]
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
        [Microsoft.AspNetCore.Mvc.ModelBinder(BinderType = typeof(DoggyDrop.Services.BinCoordinateBinder))]
        public double Longitude { get; set; }

        public IFormFile? ImageFile { get; set; }
    }
}
