using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DoggyDrop.Services
{
    public class CloudflareR2StorageService : ICloudinaryService
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".gif",
            ".heic",
            ".heif"
        };

        private readonly IAmazonS3 _s3Client;
        private readonly CloudflareR2Settings _settings;
        private readonly ILogger<CloudflareR2StorageService> _logger;

        public CloudflareR2StorageService(
            IAmazonS3 s3Client,
            IOptions<CloudflareR2Settings> settings,
            ILogger<CloudflareR2StorageService> logger)
        {
            _s3Client = s3Client;
            _settings = settings.Value;
            _logger = logger;
        }

        public Task<string?> UploadImageAsync(IFormFile file)
        {
            return UploadFileAsync(file, "profile-images");
        }

        public Task<string?> UploadTrashBinImageAsync(IFormFile file)
        {
            return UploadFileAsync(file, "trashbins");
        }

        public Task<string?> UploadWalkImageAsync(IFormFile file)
        {
            return UploadFileAsync(file, "walks");
        }

        private async Task<string?> UploadFileAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0)
            {
                return null;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                _logger.LogWarning("Unsupported image extension {Extension}.", extension);
                return null;
            }

            var key = BuildObjectKey(folderName, extension);
            await using var stream = file.OpenReadStream();

            var request = new PutObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = ResolveContentType(file.ContentType, extension),
                AutoCloseStream = false,
                Headers =
                {
                    CacheControl = "public, max-age=31536000, immutable"
                }
            };

            try
            {
                await _s3Client.PutObjectAsync(request);
                return BuildPublicUrl(key);
            }
            catch (AmazonS3Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Cloudflare R2 upload failed for bucket {BucketName}, key {Key}. Status: {StatusCode}, ErrorCode: {ErrorCode}",
                    _settings.BucketName,
                    key,
                    exception.StatusCode,
                    exception.ErrorCode);
                return null;
            }
        }

        private static string BuildObjectKey(string folderName, string extension)
        {
            var now = DateTime.UtcNow;
            return $"{folderName}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
        }

        private string BuildPublicUrl(string key)
        {
            return $"{_settings.PublicBaseUrl.TrimEnd('/')}/{key}";
        }

        private static string ResolveContentType(string? contentType, string extension)
        {
            if (!string.IsNullOrWhiteSpace(contentType) &&
                contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return contentType;
            }

            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".heic" => "image/heic",
                ".heif" => "image/heif",
                _ => "application/octet-stream"
            };
        }
    }
}
