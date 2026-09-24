using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using System.Text.Json;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class CloudinaryImageDeliveryTests
{
    [Fact]
    public void IncomingWalkTransformation_OrientsAndStripsMetadata()
    {
        var transformation = new CloudinaryDotNet.Transformation().Angle("auto").Flags("force_strip").ToString();
        Assert.Contains("a_auto", transformation, StringComparison.Ordinal);
        Assert.Contains("fl_force_strip", transformation, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingCloudinaryUploadGetsDeliveryTransformation()
    {
        var bin = new TrashBin { ImageUrl = "https://res.cloudinary.com/example/image/upload/v123/doggydrop-trashbins/bin.jpg" };
        Assert.Equal("https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v123/doggydrop-trashbins/bin.jpg", bin.FullImageUrl);
    }

    [Theory]
    [InlineData("https://res.cloudinary.com/example/image/upload/bin.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/c_fill,w_300/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/c_fill,w_300/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/q_auto,f_auto/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/q_auto,f_auto/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/c_fill,w_300/f_auto,q_auto/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/c_fill,w_300/f_auto,q_auto/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/f_auto,q_auto/c_fill,w_300/v1/bin.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/c_fill,w_300/v1/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/c_fill,f_auto/w_300,q_auto/v1/bin.jpg?download=1", "https://res.cloudinary.com/example/image/upload/c_fill,f_auto/w_300,q_auto/v1/bin.jpg?download=1")]
    [InlineData("https://res.cloudinary.com/example/image/upload/f_auto/c_fill/v1/bin.jpg?download=1", "https://res.cloudinary.com/example/image/upload/q_auto/f_auto/c_fill/v1/bin.jpg?download=1")]
    [InlineData("https://res.cloudinary.com/example/image/upload/v1/bin.jpg?download=1", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v1/bin.jpg?download=1")]
    public void CloudinaryUrlShapesPreserveAssetAndExistingTransformations(string url, string expected)
    {
        Assert.Equal(expected, CloudinaryImageDelivery.ForDisplay(url));
        Assert.Equal(expected, CloudinaryImageDelivery.ForDisplay(expected));
    }

    [Theory]
    [InlineData("/uploads/trashbins/bin.jpg")]
    [InlineData("https://images.example.com/image/upload/bin.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v123/bin.jpg")]
    public void LocalOrNonCloudinaryUrlsAreUnchanged(string url)
    {
        Assert.Equal(url, CloudinaryImageDelivery.ForDisplay(url));
    }

    [Fact]
    public void LegacyRelativeUploadRemainsLocal()
    {
        Assert.Equal("/uploads/trashbins/bin.jpg", new TrashBin { ImageUrl = "uploads/trashbins/bin.jpg" }.FullImageUrl);
    }

    [Theory]
    [InlineData("https://res.cloudinary.com/example/image/upload/fl_keep_iptc/v1/walk.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/c_fill,fl_keep_dar/v1/walk.jpg")]
    public void MetadataPreservingTransformations_AreNotDelivered(string url)
    {
        Assert.Equal(string.Empty, CloudinaryImageDelivery.ForDisplay(url));
    }

    [Theory]
    [InlineData("https://res.cloudinary.com/example/image/upload/v1/doggydrop-walks/walk.jpg", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/v1/doggydrop-walks/walk.jpg")]
    [InlineData("https://res.cloudinary.com/example/image/upload/c_fill,w_300/v1/doggydrop-walks/walk.jpg?x=1", "https://res.cloudinary.com/example/image/upload/f_auto,q_auto/c_fill,w_300/v1/doggydrop-walks/walk.jpg?x=1")]
    [InlineData("https://res.cloudinary.com/example/image/upload/q_auto/f_auto,c_fill/v1/doggydrop-walks/walk.jpg", "https://res.cloudinary.com/example/image/upload/q_auto/f_auto,c_fill/v1/doggydrop-walks/walk.jpg")]
    public void WalkPhotoDeliveryUrl_IsTransformedAndIdempotent(string original, string expected)
    {
        var photo = new WalkPhoto { ImageUrl = original };
        Assert.Equal(expected, photo.DeliveryUrl);
        Assert.Equal(expected, CloudinaryImageDelivery.ForDisplay(photo.DeliveryUrl));
    }

    [Fact]
    public void WalkMemoryAndShareAsset_DoNotContainOriginalCloudinaryUrl()
    {
        const string original = "https://res.cloudinary.com/example/image/upload/v1/doggydrop-walks/walk.jpg";
        var photo = new WalkPhoto { WalkId = 7, UserId = "owner", ImageUrl = original };
        var walk = new Walk { Id = 7, OwnerId = "owner", Status = "Completed", Photos = [photo] };

        var memory = WalkMemoryPresentation.Build(walk, [], [], [], includeOwnerDetails: true);
        var serialized = JsonSerializer.Serialize(memory);

        Assert.DoesNotContain(original, serialized, StringComparison.Ordinal);
        Assert.Equal(photo.DeliveryUrl, memory.HeroPhotoUrl);
        Assert.Equal(photo.DeliveryUrl, memory.ShareAsset?.PhotoUrl);
    }
}
