using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;

namespace DoggyDrop.Services;

public static class PlaceLogoUploadPolicy
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public static bool HasSafeDimensions(int width, int height) =>
        width is > 0 and <= 4096 && height is > 0 and <= 4096 && (long)width * height <= 16_000_000;

    public static async Task<bool> IsSupportedAsync(IFormFile? file)
    {
        if (file == null || file.Length is <= 0 or > MaxBytes) return false;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var mediaType = file.ContentType.ToLowerInvariant();
        var allowed = (extension, mediaType) is
            (".png", "image/png") or (".jpg" or ".jpeg", "image/jpeg") or (".webp", "image/webp");
        if (!allowed) return false;
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var count = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        return (extension, mediaType) switch
        {
            (".png", "image/png") => count >= 8 && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            (".jpg" or ".jpeg", "image/jpeg") => count >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            (".webp", "image/webp") => count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }
}

// Internal cleanup identity, never persisted or projected to public Place models.
// Resource/delivery type is always image/upload; the parser rejects all others.
public sealed record PlaceLogoAsset(string CloudName, string PublicId);

public static class PlaceLogoDelivery
{
    private const string UploadPath = "/image/upload/";
    private const string MarkerTransform = "c_fit,w_128,h_128/";
    private const string SafeTransform = "f_auto,q_auto/";

    public static string? ForMarker(string? url, string? expectedCloudName)
    {
        if (string.IsNullOrWhiteSpace(expectedCloudName) || !TryManagedAsset(url, expectedCloudName, out _) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return null;
        var prefix = $"/{expectedCloudName}/image/upload/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var pathUrl = uri.GetLeftPart(UriPartial.Path);
        if (TryManagedId(pathUrl, out _))
        {
            var safe = PlaceLinks.SafeImage(url);
            if (safe == null) return null;
            var index = safe.IndexOf(UploadPath, StringComparison.Ordinal);
            return safe.Insert(index + UploadPath.Length, MarkerTransform);
        }

        var markerPrefix = prefix + MarkerTransform + SafeTransform;
        if (!uri.AbsolutePath.StartsWith(markerPrefix, StringComparison.Ordinal)) return null;
        var originalPath = prefix + uri.AbsolutePath[markerPrefix.Length..];
        var originalUrl = uri.GetLeftPart(UriPartial.Authority) + originalPath;
        return TryManagedId(originalUrl, out _) ? url : null;
    }

    public static bool TryManagedId(string? rawUrl, out string publicId)
    {
        var asset = ResolveAsset(rawUrl, null, out _, allowDeliveryAliases: false);
        publicId = asset?.PublicId ?? string.Empty;
        return asset != null;
    }

    public static bool TryManagedAsset(string? url, string? expectedCloudName, out PlaceLogoAsset asset)
    {
        asset = ResolveAsset(url, expectedCloudName, out _)!;
        return asset != null;
    }

    // The sole managed-path parser. Strict upload/public wrappers keep their existing
    // policy; cleanup also recognizes the exact delivery transforms emitted by this app.
    // Unresolved Cloudinary references must retain candidates, never prove absence.
    internal static PlaceLogoAsset? ResolveAsset(string? rawUrl, string? expectedCloudName,
        out bool uncertain, bool allowDeliveryAliases = true)
    {
        uncertain = false;
        if (string.IsNullOrEmpty(rawUrl)) return null;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            uncertain = rawUrl.Contains("cloudinary.com", StringComparison.OrdinalIgnoreCase);
            return null;
        }
        if (!uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase)) return null;
        uncertain = true;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            rawUrl.Any(char.IsWhiteSpace) || rawUrl.Any(char.IsControl) || rawUrl.Contains('\\')) return null;
        if (!allowDeliveryAliases && (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))) return null;

        // Uri normalizes dot segments. Inspect the original path as well so encoded
        // separators, traversal, duplicate slashes or normalized paths cannot pass.
        var authorityEnd = rawUrl.IndexOfAny(['/', '?', '#'], rawUrl.IndexOf("://", StringComparison.Ordinal) + 3);
        if (authorityEnd < 0 || rawUrl[authorityEnd] != '/') return null;
        var pathEnd = rawUrl.IndexOfAny(['?', '#'], authorityEnd);
        var path = pathEnd < 0 ? rawUrl[authorityEnd..] : rawUrl[authorityEnd..pathEnd];
        if (path.Contains('%') || path != uri.AbsolutePath) return null;
        var segments = path[1..].Split('/');
        if (segments.Length < 8 || segments[0].Length is < 1 or > 128 ||
            !segments[0].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ||
            (expectedCloudName != null && segments[0] != expectedCloudName) ||
            segments[1] != "image" || segments[2] != "upload") return null;
        var versionIndex = 3;
        if (allowDeliveryAliases && segments[3] == MarkerTransform.TrimEnd('/') &&
            segments[4] == SafeTransform.TrimEnd('/')) versionIndex = 5;
        else if (allowDeliveryAliases && segments[3] == SafeTransform.TrimEnd('/')) versionIndex = 4;
        if (segments.Length != versionIndex + 5 || segments[versionIndex].Length < 2 ||
            segments[versionIndex][0] != 'v' || !segments[versionIndex][1..].All(char.IsAsciiDigit) ||
            segments[versionIndex + 1] != "doggydrop" || segments[versionIndex + 2] != "places" ||
            segments[versionIndex + 3] != "logos") return null;
        var file = segments[versionIndex + 4];
        if (!file.EndsWith(".webp", StringComparison.Ordinal) ||
            file.Length != 37 || !Guid.TryParseExact(file[..^5], "N", out _)) return null;
        uncertain = false;
        return new PlaceLogoAsset(segments[0], $"doggydrop/places/logos/{file[..^5]}");
    }
}

public interface IPlaceLogoStorage
{
    Task<string?> UploadAsync(IFormFile file);
    Task DeleteManagedAsync(string? url);
}

public sealed record PlaceLogoCloudName(string? Value);

public sealed class MissingPlaceLogoStorage : IPlaceLogoStorage
{
    public Task<string?> UploadAsync(IFormFile file) => Task.FromResult<string?>(null);
    public Task DeleteManagedAsync(string? url) => Task.CompletedTask;
}

public sealed record PlaceLogoUploadResponse(string? PublicId, string? SecureUrl, bool Created);

public interface IPlaceLogoCloudinaryClient
{
    string CloudName { get; }
    Task<PlaceLogoUploadResponse> UploadAsync(Stream content, string publicId);
    Task DeleteAsync(string publicId);
}

public sealed class CloudinaryPlaceLogoClient(Cloudinary cloudinary) : IPlaceLogoCloudinaryClient
{
    public string CloudName => cloudinary.Api.Account.Cloud;

    public async Task<PlaceLogoUploadResponse> UploadAsync(Stream content, string publicId)
    {
        var result = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription("logo.webp", content),
            PublicId = publicId,
            Overwrite = false,
            UniqueFilename = false
        });
        return new PlaceLogoUploadResponse(result.PublicId, result.SecureUrl?.ToString(), result.Error == null);
    }

    public async Task DeleteAsync(string publicId)
    {
        var result = await cloudinary.DestroyAsync(new DeletionParams(publicId));
        if (result.Error != null) throw new InvalidOperationException("Cloudinary could not delete the place logo.");
    }
}

public sealed class CloudinaryPlaceLogoStorage(
    IPlaceLogoCloudinaryClient cloudinary, IImageOptimizationService optimizer,
    ILogger<CloudinaryPlaceLogoStorage> logger) : IPlaceLogoStorage
{
    public async Task<string?> UploadAsync(IFormFile file)
    {
        if (!await PlaceLogoUploadPolicy.IsSupportedAsync(file)) return null;
        await using var input = file.OpenReadStream();
        var optimized = await optimizer.OptimizeAsync(input, file.ContentType, file.FileName, ImageOptimizationPreset.PlaceLogo);
        await using var content = optimized.Content;
        if (!optimized.WasOptimized) return null;
        var id = $"doggydrop/places/logos/{Guid.NewGuid():N}";
        var result = await cloudinary.UploadAsync(content, id);
        var uploaded = string.IsNullOrEmpty(cloudinary.CloudName) ? null :
            PlaceLogoDelivery.ResolveAsset(result.SecureUrl, cloudinary.CloudName, out _, allowDeliveryAliases: false);
        if (result.Created && result.PublicId == id && uploaded?.PublicId == id)
            return result.SecureUrl;

        if (result.Created && result.PublicId == id)
        {
            try { await cloudinary.DeleteAsync(id); }
            catch (Exception exception) { logger.LogWarning(exception, "Rejected place logo response left a possible orphan."); }
        }
        logger.LogWarning("Cloudinary place logo upload response was rejected.");
        return null;
    }

    public async Task DeleteManagedAsync(string? url)
    {
        if (string.IsNullOrEmpty(cloudinary.CloudName) ||
            !PlaceLogoDelivery.TryManagedAsset(url, cloudinary.CloudName, out var asset)) return;
        await cloudinary.DeleteAsync(asset.PublicId);
    }
}
