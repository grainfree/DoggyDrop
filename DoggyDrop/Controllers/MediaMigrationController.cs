using Amazon.S3;
using Amazon.S3.Model;
using DoggyDrop.Data;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace DoggyDrop.Controllers
{
    [Authorize(Roles = "Admin")]
    [Route("Admin/MediaMigration")]
    public class MediaMigrationController : Controller
    {
        private const string CloudinaryHostMarker = "cloudinary.com";
        private readonly ApplicationDbContext _context;
        private readonly IAmazonS3? _s3Client;
        private readonly CloudflareR2Settings _r2Settings;
        private readonly HttpClient _httpClient;
        private readonly IImageOptimizationService _imageOptimizationService;
        private readonly ILogger<MediaMigrationController> _logger;

        public MediaMigrationController(
            ApplicationDbContext context,
            IServiceProvider serviceProvider,
            IOptions<CloudflareR2Settings> r2Settings,
            IHttpClientFactory httpClientFactory,
            IImageOptimizationService imageOptimizationService,
            ILogger<MediaMigrationController> logger)
        {
            _context = context;
            _s3Client = serviceProvider.GetService<IAmazonS3>();
            _r2Settings = r2Settings.Value;
            _httpClient = httpClientFactory.CreateClient(nameof(MediaMigrationController));
            _imageOptimizationService = imageOptimizationService;
            _logger = logger;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            return View(await BuildViewModelAsync());
        }

        [HttpPost("Run")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Run(int batchLimit = 25)
        {
            batchLimit = Math.Clamp(batchLimit, 1, 100);
            var model = await BuildViewModelAsync(batchLimit);
            if (!_r2Settings.IsConfigured)
            {
                model.Message = "Cloudflare R2 ni konfiguriran. Preveri Render env spremenljivke za R2 in ponovno deployaj.";
                return View("Index", model);
            }

            var results = new List<MediaMigrationResultViewModel>();

            foreach (var item in model.PendingItems.Take(batchLimit))
            {
                results.Add(await MigrateItemAsync(item));
            }

            await _context.SaveChangesAsync();

            var refreshed = await BuildViewModelAsync(batchLimit);
            refreshed.Results = results;
            refreshed.MigratedCount = results.Count(result => result.Status == "Migrated");
            refreshed.FailedCount = results.Count(result => result.Status == "Failed");
            refreshed.MissingCount = results.Count(result => result.Status == "Missing");
            refreshed.Message = $"Batch koncan: {refreshed.MigratedCount} migriranih, {refreshed.MissingCount} manjkajocih, {refreshed.FailedCount} neuspesnih.";
            return View("Index", refreshed);
        }

        [HttpPost("OptimizeR2")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OptimizeR2(int batchLimit = 25)
        {
            batchLimit = Math.Clamp(batchLimit, 1, 100);
            var model = await BuildViewModelAsync(batchLimit);
            if (!_r2Settings.IsConfigured)
            {
                model.Message = "Cloudflare R2 ni konfiguriran. Preveri Render env spremenljivke za R2 in ponovno deployaj.";
                return View("Index", model);
            }

            var results = new List<MediaMigrationResultViewModel>();
            var items = (await LoadMediaItemsAsync())
                .Where(item => IsR2Url(item.Url) && !IsOptimizedR2Url(item.Url))
                .OrderBy(item => item.SourceType)
                .ThenBy(item => item.EntityId)
                .Take(batchLimit)
                .ToList();

            foreach (var item in items)
            {
                results.Add(await OptimizeR2ItemAsync(item));
            }

            await _context.SaveChangesAsync();

            var refreshed = await BuildViewModelAsync(batchLimit);
            refreshed.Results = results;
            refreshed.MigratedCount = results.Count(result => result.Status == "Optimized");
            refreshed.FailedCount = results.Count(result => result.Status == "Failed");
            refreshed.Message = $"R2 optimizacija koncana: {refreshed.MigratedCount} optimiziranih, {refreshed.FailedCount} neuspesnih.";
            return View("Index", refreshed);
        }

        private async Task<MediaMigrationViewModel> BuildViewModelAsync(int batchLimit = 25)
        {
            var items = await LoadMediaItemsAsync();
            return new MediaMigrationViewModel
            {
                CloudinaryCount = items.Count(item => IsCloudinaryUrl(item.Url)),
                R2Count = items.Count(item => IsR2Url(item.Url)),
                R2OptimizationPendingCount = items.Count(item => IsR2Url(item.Url) && !IsOptimizedR2Url(item.Url)),
                LocalCount = items.Count(item => IsLocalUrl(item.Url)),
                EmptyCount = items.Count(item => string.IsNullOrWhiteSpace(item.Url)),
                IsR2Configured = _r2Settings.IsConfigured,
                BatchLimit = batchLimit,
                PendingItems = items
                    .Where(item => IsCloudinaryUrl(item.Url))
                    .OrderBy(item => item.SourceType)
                    .ThenBy(item => item.EntityId)
                    .Take(100)
                    .ToList()
            };
        }

        private async Task<List<MediaMigrationItemViewModel>> LoadMediaItemsAsync()
        {
            var items = new List<MediaMigrationItemViewModel>();

            items.AddRange(await _context.Users
                .Select(user => new MediaMigrationItemViewModel
                {
                    SourceType = "User profile",
                    EntityKey = user.Id,
                    Url = user.ProfileImageUrl ?? string.Empty
                })
                .ToListAsync());

            items.AddRange(await _context.Dogs
                .Select(dog => new MediaMigrationItemViewModel
                {
                    SourceType = "Dog",
                    EntityId = dog.Id,
                    Url = dog.PhotoUrl ?? string.Empty
                })
                .ToListAsync());

            items.AddRange(await _context.TrashBins
                .Select(bin => new MediaMigrationItemViewModel
                {
                    SourceType = "Trash bin",
                    EntityId = bin.Id,
                    Url = bin.ImageUrl ?? string.Empty
                })
                .ToListAsync());

            items.AddRange(await _context.WalkPhotos
                .Select(photo => new MediaMigrationItemViewModel
                {
                    SourceType = "Walk photo",
                    EntityId = photo.Id,
                    Url = photo.ImageUrl ?? string.Empty
                })
                .ToListAsync());

            return items;
        }

        private async Task<MediaMigrationResultViewModel> MigrateItemAsync(MediaMigrationItemViewModel item)
        {
            try
            {
                var newUrl = await CopyRemoteImageToR2Async(item);
                await UpdateDatabaseUrlAsync(item, newUrl);

                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    NewUrl = newUrl,
                    Status = "Migrated"
                };
            }
            catch (MissingRemoteMediaException exception)
            {
                await ClearDatabaseUrlAsync(item);
                _logger.LogInformation(exception, "Cloudinary media missing for {SourceType} {EntityId}", item.SourceType, item.EntityId);
                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    Status = "Missing",
                    Error = exception.Message
                };
            }
            catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                await ClearDatabaseUrlAsync(item);
                _logger.LogInformation(exception, "Cloudinary media missing for {SourceType} {EntityId}", item.SourceType, item.EntityId);
                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    Status = "Missing",
                    Error = $"Cloudinary slika ne obstaja vec ({(int)exception.StatusCode.Value})."
                };
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Cloudinary media migration failed for {SourceType} {EntityId}", item.SourceType, item.EntityId);
                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    Status = "Failed",
                    Error = exception.Message
                };
            }
        }

        private async Task<MediaMigrationResultViewModel> OptimizeR2ItemAsync(MediaMigrationItemViewModel item)
        {
            try
            {
                var newUrl = await CopyRemoteImageToR2Async(item, forceOptimizedFolder: true);
                await UpdateDatabaseUrlAsync(item, newUrl);

                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    NewUrl = newUrl,
                    Status = "Optimized"
                };
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "R2 media optimization failed for {SourceType} {EntityId}", item.SourceType, item.EntityId);
                return new MediaMigrationResultViewModel
                {
                    SourceType = item.SourceType,
                    EntityId = item.EntityId,
                    EntityKey = item.EntityKey,
                    OldUrl = item.Url,
                    Status = "Failed",
                    Error = exception.Message
                };
            }
        }

        private async Task<string> CopyRemoteImageToR2Async(MediaMigrationItemViewModel item, bool forceOptimizedFolder = false)
        {
            if (_s3Client == null || !_r2Settings.IsConfigured)
            {
                throw new InvalidOperationException("Cloudflare R2 ni konfiguriran.");
            }

            using var response = await _httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                throw new MissingRemoteMediaException($"Cloudinary slika ne obstaja vec ({(int)response.StatusCode}).");
            }

            response.EnsureSuccessStatusCode();

            await using var remoteStream = await response.Content.ReadAsStreamAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            var optimizedImage = await _imageOptimizationService.OptimizeAsync(
                remoteStream,
                contentType,
                item.Url,
                ResolveOptimizationPreset(item.SourceType));
            await using var uploadStream = optimizedImage.Content;
            var key = BuildObjectKey(item.SourceType, item.EntityId, item.EntityKey, optimizedImage.Extension, forceOptimizedFolder || optimizedImage.WasOptimized);

            var request = new PutObjectRequest
            {
                BucketName = _r2Settings.BucketName,
                Key = key,
                InputStream = uploadStream,
                ContentType = optimizedImage.ContentType,
                AutoCloseStream = false,
                DisableDefaultChecksumValidation = true,
                DisablePayloadSigning = true,
                Headers =
                {
                    ContentLength = uploadStream.Length,
                    CacheControl = "public, max-age=31536000, immutable"
                }
            };

            await _s3Client.PutObjectAsync(request);
            return $"{_r2Settings.PublicBaseUrl.TrimEnd('/')}/{key}";
        }

        private async Task UpdateDatabaseUrlAsync(MediaMigrationItemViewModel item, string newUrl)
        {
            switch (item.SourceType)
            {
                case "User profile":
                    var user = await _context.Users.FirstOrDefaultAsync(candidate => candidate.Id == item.EntityKey);
                    if (user != null)
                    {
                        user.ProfileImageUrl = newUrl;
                    }
                    break;
                case "Dog":
                    var dog = await _context.Dogs.FindAsync(item.EntityId);
                    if (dog != null && dog.PhotoUrl == item.Url)
                    {
                        dog.PhotoUrl = newUrl;
                    }
                    break;
                case "Trash bin":
                    var bin = await _context.TrashBins.FindAsync(item.EntityId);
                    if (bin != null && bin.ImageUrl == item.Url)
                    {
                        bin.ImageUrl = newUrl;
                    }
                    break;
                case "Walk photo":
                    var photo = await _context.WalkPhotos.FindAsync(item.EntityId);
                    if (photo != null && photo.ImageUrl == item.Url)
                    {
                        photo.ImageUrl = newUrl;
                    }
                    break;
            }
        }

        private async Task ClearDatabaseUrlAsync(MediaMigrationItemViewModel item)
        {
            switch (item.SourceType)
            {
                case "User profile":
                    var user = await _context.Users.FirstOrDefaultAsync(candidate => candidate.Id == item.EntityKey);
                    if (user != null && user.ProfileImageUrl == item.Url)
                    {
                        user.ProfileImageUrl = null;
                    }
                    break;
                case "Dog":
                    var dog = await _context.Dogs.FindAsync(item.EntityId);
                    if (dog != null && dog.PhotoUrl == item.Url)
                    {
                        dog.PhotoUrl = null;
                    }
                    break;
                case "Trash bin":
                    var bin = await _context.TrashBins.FindAsync(item.EntityId);
                    if (bin != null && bin.ImageUrl == item.Url)
                    {
                        bin.ImageUrl = null;
                    }
                    break;
                case "Walk photo":
                    var photo = await _context.WalkPhotos.FindAsync(item.EntityId);
                    if (photo != null && photo.ImageUrl == item.Url)
                    {
                        photo.ImageUrl = string.Empty;
                    }
                    break;
            }
        }

        private static string BuildObjectKey(string sourceType, int? entityId, string? entityKey, string extension, bool optimized)
        {
            var folderPrefix = sourceType switch
            {
                "User profile" => "profile-images",
                "Dog" => "dogs",
                "Trash bin" => "trashbins",
                "Walk photo" => "walks",
                _ => "media"
            };

            var folder = optimized ? $"{folderPrefix}/optimized" : $"{folderPrefix}/migrated";
            var idPart = entityId?.ToString() ?? SanitizeKeyPart(entityKey) ?? "user";
            return $"{folder}/{DateTime.UtcNow:yyyy/MM}/{idPart}-{Guid.NewGuid():N}{extension}";
        }

        private static ImageOptimizationPreset ResolveOptimizationPreset(string sourceType)
        {
            return sourceType switch
            {
                "User profile" or "Dog" => ImageOptimizationPreset.Profile,
                "Walk photo" => ImageOptimizationPreset.Walk,
                _ => ImageOptimizationPreset.TrashBin
            };
        }

        private static string? SanitizeKeyPart(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var safeChars = value
                .Where(character => char.IsLetterOrDigit(character) || character == '-' || character == '_')
                .ToArray();

            return safeChars.Length == 0 ? null : new string(safeChars);
        }

        private static string ResolveExtension(string url, string contentType)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
            var extension = Path.GetExtension(path);
            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 6)
            {
                return extension.ToLowerInvariant();
            }

            return contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                "image/avif" => ".avif",
                "image/heic" => ".heic",
                "image/heif" => ".heif",
                _ => ".jpg"
            };
        }

        private bool IsR2Url(string? url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && !string.IsNullOrWhiteSpace(_r2Settings.PublicBaseUrl)
                && url.StartsWith(_r2Settings.PublicBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOptimizedR2Url(string? url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && IsR2Url(url)
                && (url.Contains("/optimized/", StringComparison.OrdinalIgnoreCase)
                    || url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCloudinaryUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && url.Contains(CloudinaryHostMarker, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url)
                && url.StartsWith("/", StringComparison.Ordinal);
        }

        private sealed class MissingRemoteMediaException : Exception
        {
            public MissingRemoteMediaException(string message)
                : base(message)
            {
            }
        }
    }
}
