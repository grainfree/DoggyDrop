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
