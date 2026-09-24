namespace DoggyDrop.Services;

public static class WalkPhotoUploadPolicy
{
    // 12 MiB admits modern phone photos while keeping multipart processing bounded.
    public const long MaxBytes = 12L * 1024 * 1024;
    // 50 MP admits 8064x6048 phone stills but bounds a four-byte decode to ~200 MiB.
    public const long MaxPixels = 50_000_000;
    public const int MaxDimension = 12_000;

    public static bool HasSafeDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension) return false;
        try
        {
            return checked((long)width * height) <= MaxPixels;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
