using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace DoggyDrop.Controllers
{
    public class CloudinaryTestController : Controller
    {
        private readonly ICloudinaryService _cloudinaryService;

        public CloudinaryTestController(ICloudinaryService cloudinaryService)
        {
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet]
        public IActionResult Upload()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file != null)
            {
                var imageUrl = await _cloudinaryService.UploadTrashBinImageAsync(file);

                if (!string.IsNullOrEmpty(imageUrl))
                {
                    ViewBag.Message = "✅ Slika uspešno naložena!";
                    ViewBag.ImageUrl = imageUrl;
                }
                else
                {
                    ViewBag.Message = "❌ Napaka pri nalaganju slike.";
                }
            }
            else
            {
                ViewBag.Message = "⚠️ Niste izbrali datoteke.";
            }

            return View();
        }

        [HttpGet]
        public IActionResult Settings()
        {
            var cloudName = Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME");
            var apiKey = Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY");
            var apiSecret = Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET");

            ViewBag.CloudName = cloudName;
            ViewBag.ApiKey = apiKey;
            ViewBag.ApiSecret = apiSecret;

            return View();
        }

    }
}
