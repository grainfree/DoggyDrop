using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System;

namespace DoggyDrop.Services
{
    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<CloudinaryService> _logger;
        private readonly IImageOptimizationService _imageOptimizationService;

        public CloudinaryService(
            Cloudinary cloudinary,
            IWebHostEnvironment environment,
            ILogger<CloudinaryService> logger,
            IImageOptimizationService imageOptimizationService)
        {
            _cloudinary = cloudinary;
            _environment = environment;
            _logger = logger;
            _imageOptimizationService = imageOptimizationService;
        }

        // ✅ Nalaganje profilne slike
        public async Task<string?> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Console.WriteLine("⚠️ Profilna slika: prazna datoteka.");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-profile-images",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            Console.WriteLine("🌩️ Rezultat nalaganja (profilna slika):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            if (uploadResult.SecureUrl != null)
            {
                return uploadResult.SecureUrl.ToString();
            }

            _logger.LogWarning("Cloudinary profile upload failed. Falling back to local storage. Error: {Error}", uploadResult.Error?.Message);
            return await SaveLocalImageAsync(file, "profile-images");
        }

        // ✅ Nalaganje slike koša
        public async Task<string?> UploadTrashBinImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                Console.WriteLine("⚠️ Slika koša: prazna datoteka.");
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-trashbins",
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            Console.WriteLine("🌩️ Rezultat nalaganja (slika koša):");
            Console.WriteLine($"StatusCode: {uploadResult.StatusCode}");
            Console.WriteLine($"SecureUrl: {uploadResult.SecureUrl}");
            Console.WriteLine($"Error: {uploadResult.Error?.Message}");

            if (uploadResult.SecureUrl != null)
            {
                return uploadResult.SecureUrl.ToString();
            }

            _logger.LogWarning("Cloudinary trash bin upload failed. Falling back to local storage. Error: {Error}", uploadResult.Error?.Message);
            return await SaveLocalImageAsync(file, "trashbins");
        }

        public async Task<string?> UploadWalkImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0 || file.Length > WalkPhotoUploadPolicy.MaxBytes)
            {
                return null;
            }

            await using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = "doggydrop-walks",
                Transformation = new Transformation().Angle("auto").Flags("force_strip"),
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);
            if (uploadResult.SecureUrl != null)
            {
                return uploadResult.SecureUrl.ToString();
            }

            _logger.LogWarning("Cloudinary walk upload failed. Falling back to local storage. Error: {Error}", uploadResult.Error?.Message);
            return await SaveSanitizedWalkImageLocallyAsync(file);
        }

        private async Task<string?> SaveSanitizedWalkImageLocallyAsync(IFormFile file)
        {
            await using var input = file.OpenReadStream();
            var optimized = await _imageOptimizationService.OptimizeAsync(input, file.ContentType, file.FileName, ImageOptimizationPreset.Walk);
            await using var content = optimized.Content;
            if (!optimized.WasOptimized)
            {
                _logger.LogWarning("Walk image could not be sanitized; local fallback upload was rejected.");
                return null;
            }

            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "walks");
            Directory.CreateDirectory(uploadRoot);
            var fileName = $"{Guid.NewGuid():N}{optimized.Extension}";
            await using var output = File.Create(Path.Combine(uploadRoot, fileName));
            await content.CopyToAsync(output);
            return $"/uploads/walks/{fileName}";
        }

        private async Task<string?> SaveLocalImageAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic", ".heif" };
            if (!allowedExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported image extension {Extension}.", extension);
                return null;
            }

            var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", folderName);
            Directory.CreateDirectory(uploadRoot);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadRoot, fileName);

            await using var stream = File.Create(filePath);
            await file.CopyToAsync(stream);

            return $"/uploads/{folderName}/{fileName}";
        }
    }
}
