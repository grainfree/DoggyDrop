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
        Walk,
        PlaceLogo
    }

    public sealed class OptimizedImage
    {
        public Stream Content { get; init; } = Stream.Null;

        public string ContentType { get; init; } = "application/octet-stream";

        public string Extension { get; init; } = ".jpg";

        public bool WasOptimized { get; init; }
    }

    public class ImageOptimizationService : IImageOptimizationService, IBinPhotoProcessor
    {
        public Task<OptimizedImage> OptimizeAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset) =>
            ProcessAsync(input, contentType, fileName, preset, 0);

        public Task<OptimizedImage> RotateAsync(Stream input, string contentType, string fileName, int clockwiseDegrees)
        {
            if (clockwiseDegrees is not (90 or 180 or 270)) throw new ArgumentOutOfRangeException(nameof(clockwiseDegrees));
            return ProcessAsync(input, contentType, fileName, ImageOptimizationPreset.TrashBin, clockwiseDegrees);
        }

        private async Task<OptimizedImage> ProcessAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset, int rotation)
        {
            using var original = new MemoryStream();
            var bin = preset == ImageOptimizationPreset.TrashBin;
            // Profile/dog originals must not bypass pixel re-encoding on decode failure.
            var strict = preset is ImageOptimizationPreset.Profile or ImageOptimizationPreset.Walk or ImageOptimizationPreset.PlaceLogo or ImageOptimizationPreset.TrashBin;
            var maxBytes = bin ? BinPhotoUploadPolicy.MaxBytes : preset == ImageOptimizationPreset.PlaceLogo ? PlaceLogoUploadPolicy.MaxBytes : WalkPhotoUploadPolicy.MaxBytes;
            if (strict && input.CanSeek && input.Length - input.Position > maxBytes)
                return RejectedWalkImage();

            var buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                if (strict && original.Length + read > maxBytes)
                    return RejectedWalkImage();
                await original.WriteAsync(buffer.AsMemory(0, read));
            }
            original.Position = 0;
            if (bin && !BinPhotoUploadPolicy.Matches(original.GetBuffer().AsSpan(0, (int)original.Length), contentType, fileName))
                return RejectedWalkImage();

            try
            {
                using var logoCodecStream = preset == ImageOptimizationPreset.PlaceLogo || bin
                    ? new MemoryStream(original.ToArray()) : null;
                using var codec = SKCodec.Create(logoCodecStream ?? original);
                if (strict && (codec == null ||
                    !(preset == ImageOptimizationPreset.PlaceLogo
                        ? PlaceLogoUploadPolicy.HasSafeDimensions(codec.Info.Width, codec.Info.Height)
                        : bin ? BinPhotoUploadPolicy.HasSafeDimensions(codec.Info.Width, codec.Info.Height)
                        : WalkPhotoUploadPolicy.HasSafeDimensions(codec.Info.Width, codec.Info.Height))))
                    return RejectedWalkImage();
                var origin = codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
                if (preset != ImageOptimizationPreset.PlaceLogo && !bin) original.Position = 0;

                using var bitmap = bin ? DecodeBin(codec!) : preset == ImageOptimizationPreset.PlaceLogo
                    ? SKBitmap.Decode(original.ToArray())
                    : SKBitmap.Decode(original);
                if (bitmap == null)
                {
                    return strict
                        ? RejectedWalkImage()
                        : BuildFallback(original, contentType, fileName);
                }

                using var orientedBitmap = ApplyEncodedOrigin(bitmap, origin);
                var oriented = orientedBitmap ?? bitmap;
                using var rotatedBitmap = rotation == 0 ? null : ApplyEncodedOrigin(oriented, rotation switch
                {
                    90 => SKEncodedOrigin.RightTop,
                    180 => SKEncodedOrigin.BottomRight,
                    _ => SKEncodedOrigin.LeftBottom
                });
                oriented = rotatedBitmap ?? oriented;
                using var trimmedBitmap = preset == ImageOptimizationPreset.PlaceLogo ? TrimTransparentEdges(oriented) : null;
                var sourceBitmap = trimmedBitmap ?? oriented;
                var settings = ResolveSettings(preset);
                var outputWidth = sourceBitmap.Width;
                var outputHeight = sourceBitmap.Height;
                if (sourceBitmap.Width > settings.MaxDimension || sourceBitmap.Height > settings.MaxDimension)
                {
                    var scale = Math.Min(
                        settings.MaxDimension / (double)sourceBitmap.Width,
                        settings.MaxDimension / (double)sourceBitmap.Height);
                    outputWidth = Math.Max(1, (int)Math.Round(sourceBitmap.Width * scale));
                    outputHeight = Math.Max(1, (int)Math.Round(sourceBitmap.Height * scale));
                }

                using var resizedBitmap = outputWidth == sourceBitmap.Width && outputHeight == sourceBitmap.Height
                    ? null
                    : ResizeBitmap(sourceBitmap, outputWidth, outputHeight);
                using var image = SKImage.FromBitmap(resizedBitmap ?? sourceBitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Webp, settings.WebpQuality);
                if (encoded == null)
                {
                    return strict
                        ? RejectedWalkImage()
                        : BuildFallback(original, contentType, fileName);
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
                return strict
                    ? RejectedWalkImage()
                    : BuildFallback(original, contentType, fileName);
            }
        }

        private static OptimizedImage RejectedWalkImage() => new() { Content = Stream.Null };

        private static SKBitmap? DecodeBin(SKCodec codec)
        {
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp)) return null;
            var bitmap = new SKBitmap(codec.Info);
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success) return bitmap;
            bitmap.Dispose();
            return null;
        }

        private static SKBitmap? TrimTransparentEdges(SKBitmap source)
        {
            if (source.AlphaType == SKAlphaType.Opaque) return null;
            var left = source.Width;
            var top = source.Height;
            var right = -1;
            var bottom = -1;
            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).Alpha == 0) continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
            if (right < left || (left == 0 && top == 0 && right == source.Width - 1 && bottom == source.Height - 1))
                return null;
            var trimmed = new SKBitmap(new SKImageInfo(right - left + 1, bottom - top + 1, source.ColorType, source.AlphaType));
            using var canvas = new SKCanvas(trimmed);
            canvas.DrawBitmap(source, new SKRect(left, top, right + 1, bottom + 1), new SKRect(0, 0, trimmed.Width, trimmed.Height));
            return trimmed;
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

        private static SKBitmap? ApplyEncodedOrigin(SKBitmap source, SKEncodedOrigin origin)
        {
            if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            {
                return null;
            }

            var swapsDimensions = origin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
            var targetWidth = swapsDimensions ? source.Height : source.Width;
            var targetHeight = swapsDimensions ? source.Width : source.Height;
            var transformed = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, source.ColorType, source.AlphaType));

            using var canvas = new SKCanvas(transformed);
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(source.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(source.Width, source.Height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, source.Height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(source.Height, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(source.Height, source.Width);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, source.Width);
                    canvas.RotateDegrees(270);
                    break;
            }

            canvas.DrawBitmap(source, 0, 0);
            canvas.Flush();
            return transformed;
        }

        private static OptimizedImage BuildFallback(MemoryStream original, string? contentType, string? fileName)
        {
            var content = new MemoryStream(original.ToArray());
            content.Position = 0;
            var extension = ResolveExtension(fileName, contentType);

            return new OptimizedImage
            {
                Content = content,
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
                ImageOptimizationPreset.PlaceLogo => (1024, 86),
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
