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
        var assetPath = url[(index + uploadPath.Length)..];
        if (assetPath.StartsWith("f_auto,q_auto/", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        // A Cloudinary transformation generates an EXIF-oriented delivery asset for old and new uploads.
        return url.Insert(index + uploadPath.Length, "f_auto,q_auto/");
    }
}
