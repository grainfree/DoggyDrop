using DoggyDrop.Services;
using DoggyDrop.Migrations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PlaceLogoTests
{
    [Theory]
    [InlineData("logo.png", "image/png", SKEncodedImageFormat.Png)]
    [InlineData("logo.jpg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("logo.webp", "image/webp", SKEncodedImageFormat.Webp)]
    public async Task AcceptedFormats_AreValidatedAndReencoded(string name, string mediaType, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        var bytes = data.ToArray();
        var file = File(bytes, name, mediaType);
        Assert.True(await PlaceLogoUploadPolicy.IsSupportedAsync(file));
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.NotNull(codec);
        using var sourceBitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(sourceBitmap);
        await using var source = file.OpenReadStream();
        var result = await new ImageOptimizationService().OptimizeAsync(source, mediaType, name, ImageOptimizationPreset.PlaceLogo);
        await using (result.Content)
        {
            Assert.True(result.WasOptimized);
            Assert.Equal(".webp", result.Extension);
            using var output = new MemoryStream();
            await result.Content.CopyToAsync(output);
            using var decoded = SKBitmap.Decode(output.ToArray());
            Assert.NotNull(decoded);
            Assert.Equal(8, decoded.Width);
        }
    }

    [Fact]
    public async Task UnsupportedSvgSpoofedImageAndOversize_AreRejected()
    {
        Assert.False(await PlaceLogoUploadPolicy.IsSupportedAsync(File("<svg/>"u8.ToArray(), "logo.svg", "image/svg+xml")));
        Assert.False(await PlaceLogoUploadPolicy.IsSupportedAsync(File("<script/>"u8.ToArray(), "logo.png", "image/png")));
        var large = new FormFile(new MemoryStream([0]), 0, PlaceLogoUploadPolicy.MaxBytes + 1, "LogoFile", "logo.png")
            { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Assert.False(await PlaceLogoUploadPolicy.IsSupportedAsync(large));
        var fake = File([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0], "logo.png", "image/png");
        Assert.True(await PlaceLogoUploadPolicy.IsSupportedAsync(fake));
        await using var source = fake.OpenReadStream();
        var rejected = await new ImageOptimizationService().OptimizeAsync(source, "image/png", "logo.png", ImageOptimizationPreset.PlaceLogo);
        await using (rejected.Content) Assert.False(rejected.WasOptimized);
    }

    [Fact]
    public async Task TransparentPaddingIsTrimmedButOpaqueWhiteBackgroundIsKept()
    {
        using var padded = new SKBitmap(20, 20);
        padded.Erase(SKColors.Transparent);
        padded.SetPixel(8, 9, SKColors.Blue);
        padded.SetPixel(11, 12, SKColors.Blue);
        using var image = SKImage.FromBitmap(padded);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var source = new MemoryStream(png.ToArray());
        var result = await new ImageOptimizationService().OptimizeAsync(source, "image/png", "logo.png", ImageOptimizationPreset.PlaceLogo);
        await using (result.Content)
        {
            using var output = new MemoryStream();
            await result.Content.CopyToAsync(output);
            using var decoded = SKBitmap.Decode(output.ToArray());
            Assert.NotNull(decoded);
            Assert.Equal(4, decoded.Width);
            Assert.Equal(4, decoded.Height);
        }

        using var opaque = new SKBitmap(20, 20);
        opaque.Erase(SKColors.White);
        opaque.SetPixel(10, 10, SKColors.Blue);
        using var opaqueImage = SKImage.FromBitmap(opaque);
        using var opaquePng = opaqueImage.Encode(SKEncodedImageFormat.Png, 100);
        await using var opaqueSource = new MemoryStream(opaquePng.ToArray());
        var opaqueResult = await new ImageOptimizationService().OptimizeAsync(opaqueSource, "image/png", "logo.png", ImageOptimizationPreset.PlaceLogo);
        await using (opaqueResult.Content)
        {
            using var output = new MemoryStream();
            await opaqueResult.Content.CopyToAsync(output);
            using var decoded = SKBitmap.Decode(output.ToArray());
            Assert.NotNull(decoded);
            Assert.Equal(20, decoded.Width);
            Assert.Equal(20, decoded.Height);
        }
    }

    [Fact]
    public void DeliveryIsBoundedAndDeletionOnlyAcceptsManagedNamespace()
    {
        var id = new string('a', 32);
        var raw = $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{id}.webp";
        Assert.Equal($"doggydrop/places/logos/{id}", ManagedId(raw));
        var marker = PlaceLogoDelivery.ForMarker(raw, "test");
        Assert.Contains("c_fit,w_128,h_128/", marker);
        Assert.Contains("f_auto,q_auto/", marker);
        Assert.Equal(marker, PlaceLogoDelivery.ForMarker(marker, "test"));
        Assert.Equal(marker, PlaceLogoDelivery.ForMarker(PlaceLogoDelivery.ForMarker(raw, "test"), "test"));
        Assert.EndsWith("?x=1", PlaceLogoDelivery.ForMarker(raw + "?x=1", "test"));
        Assert.EndsWith("?x=1", PlaceLogoDelivery.ForMarker(marker + "?x=1", "test"));
        foreach (var invalid in new[] { "https://example.com/logo.webp", "https://res.cloudinary.com/test/image/upload/v123/other/logo.webp",
            raw.Replace("/test/", "/other-account/", StringComparison.Ordinal) })
        {
            Assert.Null(PlaceLogoDelivery.ForMarker(invalid, "test"));
        }
        Assert.False(PlaceLogoDelivery.TryManagedId(raw + "?x=1", out _));
        Assert.False(PlaceLogoDelivery.TryManagedId(marker, out _));
        Assert.Null(PlaceLogoDelivery.ForMarker(raw, "other-account"));
    }

    [Fact]
    public async Task RejectedUploadUrlDeletesOnlyTheGeneratedManagedId()
    {
        var client = new FakeCloudinaryClient { Response = id => new(id,
            $"https://res.cloudinary.com/test/image/upload/{id}.webp", true) };
        Assert.Null(await Storage(client).UploadAsync(ValidLogo()));
        Assert.Equal(client.UploadedId, Assert.Single(client.Deleted));
    }

    [Fact]
    public async Task MismatchedUploadIdNeverTriggersDeletion()
    {
        var client = new FakeCloudinaryClient { Response = id => new("another/account/id",
            $"https://res.cloudinary.com/test/image/upload/v123/{id}.webp", true) };
        Assert.Null(await Storage(client).UploadAsync(ValidLogo()));
        Assert.Empty(client.Deleted);
    }

    [Fact]
    public async Task FailedRejectedResponseCleanupRetainsUploadFailure()
    {
        var client = new FakeCloudinaryClient { Response = id => new(id, "not-a-url", true), FailDelete = true };
        Assert.Null(await Storage(client).UploadAsync(ValidLogo()));
        Assert.Equal(client.UploadedId, Assert.Single(client.Deleted));
    }

    [Fact]
    public async Task ValidUploadResponseDoesNotTriggerCleanup()
    {
        var client = new FakeCloudinaryClient { Response = id => new(id,
            $"https://res.cloudinary.com/test/image/upload/v123/{id}.webp", true) };
        var url = await Storage(client).UploadAsync(ValidLogo());
        Assert.NotNull(url);
        Assert.Empty(client.Deleted);
    }

    [Fact]
    public void MigrationOnlyAddsNullableBoundedLogoUrl()
    {
        var migration = new AddPlaceLogo();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(AddPlaceLogo).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(migration, [builder]);
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(builder.Operations));
        Assert.Equal("Places", column.Table);
        Assert.Equal("LogoUrl", column.Name);
        Assert.True(column.IsNullable);
        Assert.Equal(500, column.MaxLength);
    }

    [Theory]
    [InlineData(4096, 3906, true)]
    [InlineData(4096, 4096, false)]
    [InlineData(4097, 1, false)]
    [InlineData(0, 100, false)]
    public void LogoDimensionsAreBounded(int width, int height, bool accepted) =>
        Assert.Equal(accepted, PlaceLogoUploadPolicy.HasSafeDimensions(width, height));

    private static string? ManagedId(string url) => PlaceLogoDelivery.TryManagedId(url, out var id) ? id : null;

    private static CloudinaryPlaceLogoStorage Storage(FakeCloudinaryClient client) =>
        new(client, new ImageOptimizationService(), NullLogger<CloudinaryPlaceLogoStorage>.Instance);

    private static IFormFile ValidLogo()
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return File(data.ToArray(), "logo.png", "image/png");
    }

    private sealed class FakeCloudinaryClient : IPlaceLogoCloudinaryClient
    {
        public string CloudName => "test";
        public required Func<string, PlaceLogoUploadResponse> Response { get; init; }
        public string? UploadedId { get; private set; }
        public List<string> Deleted { get; } = [];
        public bool FailDelete { get; init; }
        public Task<PlaceLogoUploadResponse> UploadAsync(Stream content, string publicId)
        {
            UploadedId = publicId;
            return Task.FromResult(Response(publicId));
        }
        public Task DeleteAsync(string publicId)
        {
            Deleted.Add(publicId);
            if (FailDelete) throw new InvalidOperationException("Simulated cleanup failure.");
            return Task.CompletedTask;
        }
    }

    private static IFormFile File(byte[] bytes, string name, string mediaType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "LogoFile", name)
        { Headers = new HeaderDictionary(), ContentType = mediaType };
}
