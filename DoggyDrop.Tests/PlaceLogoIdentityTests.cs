using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PlaceLogoIdentityTests
{
    [Theory]
    [InlineData("version")] [InlineData("host")] [InlineData("transform")]
    [InlineData("query")] [InlineData("fragment")] [InlineData("safe-transform")]
    public async Task SupportedAliasesResolveToExactlyTheIdentitySentToDeletion(string kind)
    {
        var raw = BulkPlaceLogoTests.Url('a'); var alias = BulkPlaceLogoTests.Alias(kind);
        Assert.True(PlaceLogoDelivery.TryManagedAsset(raw, "test", out var expected));
        Assert.True(PlaceLogoDelivery.TryManagedAsset(alias, "test", out var actual));
        Assert.Equal(expected, actual); Assert.Equal("test", actual.CloudName);
        var cloud = new RecordingCloud(); await Storage(cloud).DeleteManagedAsync(alias);
        Assert.Equal(actual.PublicId, Assert.Single(cloud.Deleted));
        // Internal reference aliases do not broaden raw upload acceptance/public fragments.
        if (kind is "transform" or "safe-transform" or "query" or "fragment")
            Assert.False(PlaceLogoDelivery.TryManagedId(alias, out _));
        if (kind == "fragment") Assert.Null(PlaceLogoDelivery.ForMarker(alias, "test"));
    }

    public static IEnumerable<object[]> UnsafeUrls()
    {
        var raw = BulkPlaceLogoTests.Url('a');
        foreach (var url in new[]
        {
            raw.Replace("/test/", "/foreign/"),
            BulkPlaceLogoTests.Alias("transform").Replace("/test/", "/foreign/"),
            raw.Replace("/logos/", "/logos-safe/"),
            raw.Replace("/logos/", "/logos/f_auto,q_auto/"),
            raw.Replace("/logos/", "/logos/v123/"),
            raw.Replace("/upload/", "/upload/c_fill,w_12/"),
            raw.Replace("/upload/", "/private/"),
            raw.Replace("/image/", "/video/"),
            raw.Replace("/image/", "/raw/"),
            raw.Replace("/v1/", "/vNotVersion/"),
            raw.Replace("/v1/", "/"),
            raw.Replace("/places/", "/places%2flogos/"),
            raw.Replace("/logos/", "/logos/%2e%2e/logos/"),
            raw.Replace("/logos/", "/logos/../logos/"),
            raw.Replace("/logos/", "/logos//"),
            raw.Replace("aaaaaaaa", "%61aaaaaaa"),
            raw.Replace("/logos/", "/logos/%252e%252e/"),
            raw.Replace("/logos/", "/logos\\"),
            raw.Replace(".webp", ".png"),
            raw.Replace(new string('a', 32), "arbitrary"),
            raw.Replace("res.cloudinary.com", "res.cloudinary.com.evil.invalid"),
            raw.Replace("res.cloudinary.com", "evil@res.cloudinary.com"),
            raw.Replace("res.cloudinary.com", "res.cloudinary.com:444"),
            raw.Replace("https:", "http:"),
            " " + raw,
            raw + "\r\n",
            "/uploads/logo.png"
        }) yield return [url];
    }

    [Theory, MemberData(nameof(UnsafeUrls))]
    public async Task UnsafeOrForeignUrlsCannotBecomeDeletionTargets(string url)
    {
        Assert.False(PlaceLogoDelivery.TryManagedAsset(url, "test", out _));
        var cloud = new RecordingCloud(); await Storage(cloud).DeleteManagedAsync(url);
        Assert.Empty(cloud.Deleted);
    }

    [Fact]
    public void CloudAndCaseSensitivePublicIdRemainPartOfIdentity()
    {
        var raw = BulkPlaceLogoTests.Url('a');
        Assert.True(PlaceLogoDelivery.TryManagedAsset(raw, "test", out var own));
        Assert.True(PlaceLogoDelivery.TryManagedAsset(raw.Replace("/test/", "/other/"), null, out var foreign));
        Assert.NotEqual(own, foreign);
        Assert.True(PlaceLogoDelivery.TryManagedAsset(raw.Replace(new string('a', 32), new string('A', 32)), "test", out var upper));
        Assert.NotEqual(own, upper); // Cloudinary public IDs are not hostname/GUID-normalized.
    }

    [Fact]
    public async Task ForeignCloudReferenceIsNotEquivalentAndProjectionIsCapped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"logo-identity-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            await using var db = new ApplicationDbContext(options); await db.Database.EnsureCreatedAsync();
            db.Places.Add(new Place { Name = "Foreign", LogoUrl = BulkPlaceLogoTests.Url('a').Replace("/test/", "/other/") });
            await db.SaveChangesAsync();
            var reader = new PlaceLogoReferenceReader(options);
            Assert.False(await reader.IsReferencedAsync(BulkPlaceLogoTests.Url('a')));
            db.Places.AddRange(Enumerable.Range(0, PlaceLogoReferenceReader.MaxReferenceUrls)
                .Select(i => new Place { Name = "Synthetic " + i, LogoUrl = "https://external.invalid/" + i }));
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.IsReferencedAsync(BulkPlaceLogoTests.Url('a')));
        }
        finally { File.Delete(path); }
    }

    private static CloudinaryPlaceLogoStorage Storage(RecordingCloud cloud) =>
        new(cloud, new ImageOptimizationService(), NullLogger<CloudinaryPlaceLogoStorage>.Instance);

    private sealed class RecordingCloud : IPlaceLogoCloudinaryClient
    {
        public string CloudName => "test";
        public List<string> Deleted = [];
        public Task<PlaceLogoUploadResponse> UploadAsync(Stream content, string publicId) => throw new NotSupportedException();
        public Task DeleteAsync(string publicId) { Deleted.Add(publicId); return Task.CompletedTask; }
    }
}
