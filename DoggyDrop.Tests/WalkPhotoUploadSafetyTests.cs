using Amazon.Runtime;
using Amazon.S3;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Reflection;
using System.Text;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkPhotoUploadSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"doggydrop-walk-upload-{Guid.NewGuid():N}");

    [Fact]
    public async Task LocalFallback_RejectsUnprocessedOriginal()
    {
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new TestEnvironment { WebRootPath = _root }, new StubOptimizer(false));

        Assert.Null(await service.UploadWalkImageAsync(Photo()));
        Assert.False(Directory.Exists(Path.Combine(_root, "uploads", "walks")));
    }

    [Fact]
    public async Task LocalFallback_RejectsOversizedFileBeforeOptimizer()
    {
        var optimizer = new CountingOptimizer();
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new TestEnvironment { WebRootPath = _root }, optimizer);
        var photo = new FormFile(new MemoryStream([1, 2, 3]), 0,
            WalkPhotoUploadPolicy.MaxBytes + 1, "photo", "walk.jpg");

        Assert.Null(await service.UploadWalkImageAsync(photo));
        Assert.Equal(0, optimizer.Calls);
        Assert.False(Directory.Exists(Path.Combine(_root, "uploads", "walks")));
    }

    [Fact]
    public async Task LocalFallback_StoresOnlySanitizedWebp()
    {
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new TestEnvironment { WebRootPath = _root }, new StubOptimizer(true));

        var url = await service.UploadWalkImageAsync(Photo());

        Assert.StartsWith("/uploads/walks/", url);
        Assert.EndsWith(".webp", url);
        var path = Path.Combine(_root, url!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LocalFallback_RealOptimizerStoresDecodableImageWithoutSourceMetadata()
    {
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new TestEnvironment { WebRootPath = _root }, new ImageOptimizationService());

        var url = await service.UploadWalkImageAsync(PhotoWithExifMarker());

        Assert.NotNull(url);
        var path = Path.Combine(_root, url!.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.EndsWith(".webp", path);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(8, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    [Fact]
    public async Task LocalFallback_RejectsUndecodableHeicWithoutStoringOriginal()
    {
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new TestEnvironment { WebRootPath = _root }, new ImageOptimizationService());
        var bytes = Encoding.ASCII.GetBytes("unreadable-heic-content");
        var photo = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "walk.heic")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/heic"
        };

        Assert.Null(await service.UploadWalkImageAsync(photo));
        Assert.False(Directory.Exists(Path.Combine(_root, "uploads", "walks")));
    }

    [Theory]
    [InlineData(8064, 6048, true)] // 48 MP phone capture.
    [InlineData(8000, 6250, true)] // Exactly 50 MP.
    [InlineData(8001, 6250, false)]
    [InlineData(12001, 1, false)]
    [InlineData(1, 12001, false)]
    [InlineData(int.MaxValue, 1, false)]
    [InlineData(1, int.MaxValue, false)]
    [InlineData(int.MaxValue, int.MaxValue, false)]
    [InlineData(0, 100, false)]
    [InlineData(100, 0, false)]
    public void WalkPixelLimit_ValidatesDimensionsWithoutAllocatingBitmaps(int width, int height, bool expected)
    {
        Assert.Equal(expected, WalkPhotoUploadPolicy.HasSafeDimensions(width, height));
    }

    [Fact]
    public async Task WalkOptimizer_AcceptsOrdinaryImageAndRejectsMalformedInput()
    {
        var optimizer = new ImageOptimizationService();
        await using var ordinary = PhotoWithExifMarker().OpenReadStream();
        var accepted = await optimizer.OptimizeAsync(ordinary, "image/jpeg", "walk.jpg", ImageOptimizationPreset.Walk);
        await using (accepted.Content)
            Assert.True(accepted.WasOptimized);

        await using var malformed = new MemoryStream([1, 2, 3]);
        var rejected = await optimizer.OptimizeAsync(malformed, "image/jpeg", "walk.jpg", ImageOptimizationPreset.Walk);
        await using (rejected.Content)
            Assert.False(rejected.WasOptimized);
    }

    [Fact]
    public async Task WalkOptimizer_RejectsOversizedDimensionsFromCodecBeforeBitmapDecode()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        var bytes = encoded.ToArray();
        var startOfFrame = Enumerable.Range(0, bytes.Length - 9)
            .FirstOrDefault(index => bytes[index] == 0xff && bytes[index + 1] == 0xc0);
        Assert.True(startOfFrame > 0);
        // JPEG SOF stores height then width. Only the tiny fixture header is changed.
        bytes[startOfFrame + 5] = 0x17; // 6000 px
        bytes[startOfFrame + 6] = 0x70;
        bytes[startOfFrame + 7] = 0x23; // 9000 px, 54 MP
        bytes[startOfFrame + 8] = 0x28;
        using (var header = SKCodec.Create(new MemoryStream(bytes)))
        {
            Assert.NotNull(header);
            Assert.Equal(9000, header.Info.Width);
            Assert.Equal(6000, header.Info.Height);
        }

        await using var stream = new MemoryStream(bytes);
        var result = await new ImageOptimizationService().OptimizeAsync(stream, "image/jpeg", "walk.jpg", ImageOptimizationPreset.Walk);
        await using (result.Content)
            Assert.False(result.WasOptimized);
    }

    [Fact]
    public async Task R2_RejectsUnprocessedOriginalBeforeNetworkRequest()
    {
        using var client = new AmazonS3Client(new AnonymousAWSCredentials(), new AmazonS3Config
        {
            ServiceURL = "http://127.0.0.1:1",
            ForcePathStyle = true
        });
        var service = new CloudflareR2StorageService(client,
            Options.Create(new CloudflareR2Settings { BucketName = "test", PublicBaseUrl = "http://127.0.0.1:1" }),
            new StubOptimizer(false), NullLogger<CloudflareR2StorageService>.Instance);

        Assert.Null(await service.UploadWalkImageAsync(Photo()));
    }

    [Fact]
    public async Task R2_StoresOnlyReencodedImageWithoutSourceMetadata()
    {
        var client = DispatchProxy.Create<IAmazonS3, CapturingS3>();
        var capture = (CapturingS3)(object)client;
        var service = new CloudflareR2StorageService(client,
            Options.Create(new CloudflareR2Settings { BucketName = "test", PublicBaseUrl = "https://images.example.test" }),
            new ImageOptimizationService(), NullLogger<CloudflareR2StorageService>.Instance);

        var url = await service.UploadWalkImageAsync(PhotoWithExifMarker());

        Assert.StartsWith("https://images.example.test/walks/", url);
        Assert.EndsWith(".webp", url);
        var bytes = Assert.IsType<byte[]>(capture.CapturedBytes);
        Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(bytes), StringComparison.OrdinalIgnoreCase);
        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(8, decoded.Width);
    }

    private static IFormFile Photo() => new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "photo", "walk.jpg")
    {
        Headers = new HeaderDictionary(),
        ContentType = "image/jpeg"
    };

    private static IFormFile PhotoWithExifMarker()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        var source = jpeg.ToArray();
        var marker = Encoding.ASCII.GetBytes("Exif\0\0GPSLatitude");
        var segmentLength = marker.Length + 2;
        var withMetadata = new byte[source.Length + marker.Length + 4];
        source.AsSpan(0, 2).CopyTo(withMetadata);
        withMetadata[2] = 0xff;
        withMetadata[3] = 0xe1;
        withMetadata[4] = (byte)(segmentLength >> 8);
        withMetadata[5] = (byte)segmentLength;
        marker.CopyTo(withMetadata, 6);
        source.AsSpan(2).CopyTo(withMetadata.AsSpan(marker.Length + 6));
        return new FormFile(new MemoryStream(withMetadata), 0, withMetadata.Length, "photo", "walk.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(_root);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(target).StartsWith("doggydrop-walk-upload-", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class StubOptimizer(bool wasOptimized) : IImageOptimizationService
    {
        public Task<OptimizedImage> OptimizeAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset) =>
            Task.FromResult(new OptimizedImage
            {
                Content = new MemoryStream([1, 2, 3]),
                ContentType = wasOptimized ? "image/webp" : "image/jpeg",
                Extension = wasOptimized ? ".webp" : ".jpg",
                WasOptimized = wasOptimized
            });
    }

    private sealed class CountingOptimizer : IImageOptimizationService
    {
        public int Calls { get; private set; }

        public Task<OptimizedImage> OptimizeAsync(Stream input, string? contentType, string? fileName, ImageOptimizationPreset preset)
        {
            Calls++;
            throw new InvalidOperationException("Oversized input reached the optimizer.");
        }
    }

    public class CapturingS3 : DispatchProxy
    {
        public byte[]? CapturedBytes { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IAmazonS3.PutObjectAsync) || args?[0] is not Amazon.S3.Model.PutObjectRequest request)
                throw new NotSupportedException(method?.Name);
            return CaptureAsync(request);
        }

        private async Task<Amazon.S3.Model.PutObjectResponse> CaptureAsync(Amazon.S3.Model.PutObjectRequest request)
        {
            using var buffer = new MemoryStream();
            await request.InputStream.CopyToAsync(buffer);
            CapturedBytes = buffer.ToArray();
            return new Amazon.S3.Model.PutObjectResponse();
        }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
