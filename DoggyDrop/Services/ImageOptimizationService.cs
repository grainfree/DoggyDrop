using SkiaSharp;

namespace DoggyDrop.Services
{
    public interface IImageOptimizationService
    {
        Task<OptimizedImage> OptimizeAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset);
    }

    public enum ImageOptimizationPreset
    {
        Profile,
        TrashBin,
        Walk
    }

    public sealed class OptimizedImage
    {
        public Stream Content { get; init; } = Stream.Null;

        public string ContentType { get; init; } = "application/octet-stream";

        public string Extension { get; init; } = ".jpg";

        public bool WasOptimized { get; init; }
    }

    public class ImageOptimizationService : IImageOptimizationService
    {
        public async Task<OptimizedImage> OptimizeAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset)
        {
            var original = new MemoryStream();
            await input.CopyToAsync(original);
            original.Position = 0;

            try
            {
                using var bitmap = SKBitmap.Decode(original);
                if (bitmap == null)
                {
                    return BuildFallback(original, contentType, fileName);
                }

                var settings = ResolveSettings(preset);
                var outputWidth = bitmap.Width;
                var outputHeight = bitmap.Height;
                if (bitmap.Width > settings.MaxDimension || bitmap.Height > settings.MaxDimension)
                {
                    var scale = Math.Min(
                        settings.MaxDimension / (double)bitmap.Width,
                        settings.MaxDimension / (double)bitmap.Height);
                    outputWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                    outputHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                }

                using var resizedBitmap = outputWidth == bitmap.Width && outputHeight == bitmap.Height
                    ? null
                    : ResizeBitmap(bitmap, outputWidth, outputHeight);
                using var image = SKImage.FromBitmap(resizedBitmap ?? bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, settings.WebpQuality);
                if (encoded == null)
                {
                    return BuildFallback(original, contentType, fileName);
                }

                var output = new MemoryStream((int)encoded.Size);
                encoded.SaveTo(output);
                output.Position = 0;

                return new OptimizedImage
                {
                    Content = output,
                    ContentType = "image/webp",
                    Extension = ".webp",
                    WasOptimized = true
                };
            }
            catch
            {
                return BuildFallback(original, contentType, fileName);
            }
        }

        private static SKBitmap ResizeBitmap(SKBitmap source, int width, int height)
        {
            var resized = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));
            if (!source.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
            {
                resized.Dispose();
                throw new InvalidOperationException("Image resize failed.");
            }

            return resized;
        }

        private static OptimizedImage BuildFallback(MemoryStream original, string? contentType, string? fileName)
        {
            original.Position = 0;
            var extension = ResolveExtension(fileName, contentType);

            return new OptimizedImage
            {
                Content = original,
                ContentType = ResolveContentType(contentType, extension),
                Extension = extension,
                WasOptimized = false
            };
        }

        private static (int MaxDimension, int WebpQuality) ResolveSettings(ImageOptimizationPreset preset)
        {
            return preset switch
            {
                ImageOptimizationPreset.Profile => (640, 74),
                ImageOptimizationPreset.TrashBin => (1200, 76),
                ImageOptimizationPreset.Walk => (1600, 78),
                _ => (1200, 76)
            };
        }

        private static string ResolveExtension(string? fileName, string? contentType)
        {
            var extension = string.IsNullOrWhiteSpace(fileName)
                ? string.Empty
                : Path.GetExtension(fileName);

            if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 6)
            {
                return extension.ToLowerInvariant();
            }

            return contentType?.ToLowerInvariant() switch
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
                ".avif" => "image/avif",
                ".heic" => "image/heic",
                ".heif" => "image/heif",
                _ => "application/octet-stream"
            };
        }
    }
}
