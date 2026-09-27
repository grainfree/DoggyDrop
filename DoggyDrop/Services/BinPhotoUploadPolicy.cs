namespace DoggyDrop.Services;

public static class BinPhotoUploadPolicy
{
    public const long MaxBytes = 12 * 1024 * 1024;
    public const long MaxPixels = 50_000_000;
    public const int MaxDimension = 12_000;
    public const string Error = "Izberi veljavno fotografijo JPG, PNG ali WebP do 12 MiB (največ 50 milijonov pik).";
    public static bool HasSafeDimensions(int width, int height) => width is > 0 and <= MaxDimension &&
        height is > 0 and <= MaxDimension && (long)width * height <= MaxPixels;
    public static bool Matches(ReadOnlySpan<byte> bytes, string? contentType, string? fileName) =>
        (Path.GetExtension(fileName)?.ToLowerInvariant(), contentType?.ToLowerInvariant()) switch
        {
            (".jpg" or ".jpeg", "image/jpeg") => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            (".png", "image/png") => bytes.StartsWith(new byte[] {137,80,78,71,13,10,26,10}),
            (".webp", "image/webp") => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8,4).SequenceEqual("WEBP"u8),
            _ => false
        };
}

public interface IBinPhotoProcessor
{
    Task<OptimizedImage> RotateAsync(Stream input, string contentType, string fileName, int clockwiseDegrees);
}
