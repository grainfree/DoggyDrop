namespace DoggyDrop.Services;

public static class CloudinaryImageDelivery
{
    public static string ForDisplay(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.Host.Equals("res.cloudinary.com", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        const string uploadPath = "/image/upload/";
        var pathIndex = uri.AbsolutePath.IndexOf(uploadPath, StringComparison.OrdinalIgnoreCase);
        if (pathIndex < 0 || pathIndex + uploadPath.Length >= uri.AbsolutePath.Length)
        {
            return url;
        }

        var index = url.IndexOf(uploadPath, StringComparison.OrdinalIgnoreCase);
        var assetPath = uri.AbsolutePath[(pathIndex + uploadPath.Length)..];
        var segments = assetPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var transformCount = Array.FindIndex(segments, segment => IsVersion(segment));
        if (transformCount < 0) transformCount = Math.Max(0, segments.Length - 1);

        var tokens = segments.Take(transformCount).SelectMany(segment => segment.Split(','));
        var existing = tokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = new[] { "f_auto", "q_auto" }.Where(token => !existing.Contains(token)).ToArray();
        if (missing.Length == 0)
        {
            return url;
        }

        // A Cloudinary transformation generates an EXIF-oriented delivery asset for old and new uploads.
        return url.Insert(index + uploadPath.Length, string.Join(',', missing) + "/");
    }

    private static bool IsVersion(string segment) => segment.Length > 1 && segment[0] == 'v' &&
        segment.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
}
