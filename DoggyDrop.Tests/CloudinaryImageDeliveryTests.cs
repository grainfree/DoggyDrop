using DoggyDrop.Models;
using DoggyDrop.Services;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class CloudinaryImageDeliveryTests
{
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
}
