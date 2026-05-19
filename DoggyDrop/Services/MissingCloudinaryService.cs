using Microsoft.AspNetCore.Http;

namespace DoggyDrop.Services
{
    public class MissingCloudinaryService : ICloudinaryService
    {
        private readonly ILogger<MissingCloudinaryService> _logger;
        private readonly IWebHostEnvironment _environment;

        public MissingCloudinaryService(
            ILogger<MissingCloudinaryService> logger,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
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

        public Task<string?> UploadWalkImageAsync(IFormFile file)
        {
            _logger.LogWarning("Cloudinary is not configured. Saving walk image locally.");
            return SaveLocalImageAsync(file, "walks");
        }

        private async Task<string?> SaveLocalImageAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
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
