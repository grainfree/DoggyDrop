using Microsoft.AspNetCore.Http;

namespace DoggyDrop.Services
{
    public class MissingCloudinaryService : ICloudinaryService
    {
        private readonly ILogger<MissingCloudinaryService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IImageOptimizationService _imageOptimizationService;

        public MissingCloudinaryService(
            ILogger<MissingCloudinaryService> logger,
            IWebHostEnvironment environment,
            IImageOptimizationService imageOptimizationService)
        {
            _logger = logger;
            _environment = environment;
            _imageOptimizationService = imageOptimizationService;
        }

        public Task<string?> UploadImageAsync(IFormFile file)
        {
            _logger.LogWarning("Cloudinary is not configured. Saving profile image locally.");
            return SaveLocalImageAsync(file, "profile-images");
        }

        public Task<string?> UploadTrashBinImageAsync(IFormFile file)
        {
            _logger.LogWarning("Cloudinary is not configured. Saving trash bin image locally.");
            return SaveLocalImageAsync(file, "trashbins");
        }

        public async Task<string?> UploadWalkImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0 || file.Length > WalkPhotoUploadPolicy.MaxBytes) return null;

            await using var input = file.OpenReadStream();
            var optimized = await _imageOptimizationService.OptimizeAsync(input, file.ContentType, file.FileName, ImageOptimizationPreset.Walk);
            await using var content = optimized.Content;
            if (!optimized.WasOptimized)
            {
                _logger.LogWarning("Walk image could not be sanitized; local upload was rejected.");
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
