using System.Text.RegularExpressions;
using Amazon.S3;
using Amazon.S3.Model;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace DoggyDrop.Services;

public sealed record BinPhotoAsset(string Kind, string Key, string Extension);

public static class BinPhotoAssets
{
    // Only the exact namespaces historically written by DoggyDrop's bin uploaders.
    public static BinPhotoAsset? Resolve(string? url, string? cloud, string? r2Base)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Contains('%') || url.Contains('\\')) return null;
        var local = Regex.Match(url, @"^/uploads/trashbins/([a-fA-F0-9]{32})(\.(?:jpg|jpeg|png|webp))$");
        if (local.Success) return new("local", local.Groups[1].Value + local.Groups[2].Value, local.Groups[2].Value);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || url.Contains("/../") || url.Contains("/./")) return null;
        if (!string.IsNullOrEmpty(cloud) && uri.Host == "res.cloudinary.com")
        {
            var match = Regex.Match(uri.AbsolutePath, "^/" + Regex.Escape(cloud) + @"/image/upload/v[0-9]+/(doggydrop-trashbins/[a-zA-Z0-9_-]+)(\.(?:jpg|jpeg|png|webp))$");
            if (match.Success) return new("cloud", match.Groups[1].Value, match.Groups[2].Value);
        }
        if (Uri.TryCreate(r2Base?.TrimEnd('/') + "/", UriKind.Absolute, out var root) && root.Scheme == "https" &&
            uri.Authority == root.Authority && uri.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal))
        {
            var key = uri.AbsolutePath[root.AbsolutePath.Length..];
            var match = Regex.Match(key, @"^trashbins/(?:[0-9]{4}/(?:0[1-9]|1[0-2])/[a-fA-F0-9]{32}|(?:optimized|migrated)/[0-9]{4}/(?:0[1-9]|1[0-2])/[0-9]+-[a-fA-F0-9]{32})(\.(?:jpg|jpeg|png|webp))$");
            if (match.Success) return new("r2", key, match.Groups[1].Value);
        }
        return null;
    }
    public static string ContentType(string extension) => extension switch { ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg" };
}

public interface IBinPhotoStorage
{
    bool CanRotate(string? url);
    Task<string?> RotateCopyAsync(string url, int degrees);
    Task DeleteManagedAsync(string url);
}

public sealed class BinPhotoStorage(ICloudinaryService uploads, IBinPhotoProcessor processor, IWebHostEnvironment environment,
    IHttpClientFactory clients, PlaceLogoCloudName cloudName, IOptions<CloudflareR2Settings> r2Options,
    Cloudinary? cloudinary = null, IAmazonS3? r2 = null) : IBinPhotoStorage
{
    private BinPhotoAsset? Resolve(string? url) => BinPhotoAssets.Resolve(url, cloudinary == null ? null : cloudName.Value,
        r2 == null ? null : r2Options.Value.PublicBaseUrl);

    public bool CanRotate(string? url)
    {
        var asset = Resolve(url);
        try { return asset != null && (asset.Kind != "local" || LocalPath(asset.Key) is { } path && File.Exists(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task<string?> RotateCopyAsync(string url, int degrees)
    {
        var asset = Resolve(url);
        if (asset == null || degrees is not (90 or 180 or 270)) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var bytes = new MemoryStream();
        if (asset.Kind == "local")
        {
            var path = LocalPath(asset.Key);
            if (path == null || !File.Exists(path)) return null;
            await using var input = File.OpenRead(path);
            if (!await CopyBounded(input, bytes, timeout.Token)) return null;
        }
        else
        {
            // Exact allowlisted delivery URL, no redirects, no arbitrary fetch endpoint.
            using var client = clients.CreateClient("bin-photo-download");
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > BinPhotoUploadPolicy.MaxBytes) return null;
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            if (!await CopyBounded(input, bytes, timeout.Token)) return null;
        }
        bytes.Position = 0;
        var rotated = await processor.RotateAsync(bytes, BinPhotoAssets.ContentType(asset.Extension), "bin" + asset.Extension, degrees);
        await using var content = rotated.Content;
        if (!rotated.WasOptimized) return null;
        var file = new FormFile(content, 0, content.Length, "ImageFile", "rotated.webp") { Headers = new HeaderDictionary(), ContentType = "image/webp" };
        var result = await uploads.UploadTrashBinImageAsync(file);
        return result != url && Resolve(result) != null ? result : null;
    }

    public async Task DeleteManagedAsync(string url)
    {
        var asset = Resolve(url);
        if (asset == null) return;
        if (asset.Kind == "cloud")
        {
            var result = await cloudinary!.DestroyAsync(new DeletionParams(asset.Key));
            if (result.Error != null) throw new IOException("Bin photo cleanup failed.");
        }
        else if (asset.Kind == "r2")
            await r2!.DeleteObjectAsync(new DeleteObjectRequest { BucketName = r2Options.Value.BucketName, Key = asset.Key });
        else if (LocalPath(asset.Key) is { } path) File.Delete(path);
    }

    private string? LocalPath(string filename)
    {
        var root = Path.GetFullPath(environment.WebRootPath);
        var path = Path.Combine(root, "uploads", "trashbins", filename);
        // Do not follow an unexpected symlink/junction in a managed local path.
        foreach (var component in new[] { root, Path.Combine(root,"uploads"), Path.Combine(root,"uploads","trashbins"), path })
            if ((File.Exists(component) || Directory.Exists(component)) && (File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) return null;
        return path;
    }
    private static async Task<bool> CopyBounded(Stream input, Stream output, CancellationToken ct)
    {
        var buffer = new byte[81920]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > BinPhotoUploadPolicy.MaxBytes) return false;
            await output.WriteAsync(buffer.AsMemory(0,count), ct);
        }
        return output.Length > 0;
    }
}
