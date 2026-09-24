using DoggyDrop.Models;

namespace DoggyDrop.Services;

public static class PlaceCategories
{
    public static string Label(PlaceCategory category) => category switch
    {
        PlaceCategory.Veterinarian => "Veterinar",
        PlaceCategory.PetShop => "Trgovina za male živali",
        _ => "Lokacija"
    };
}

public static class PlaceLinks
{
    public static string? SafeWebsite(string? value) => SafeAbsoluteUrl(value, httpsOnly: false);

    public static string? SafeImage(string? value)
    {
        var safe = SafeAbsoluteUrl(value, httpsOnly: true);
        if (safe == null) return null;
        var delivered = CloudinaryImageDelivery.ForDisplay(safe);
        return string.IsNullOrWhiteSpace(delivered) ? null : delivered;
    }

    public static string? TelephoneHref(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 40 ||
            value.Any(character => !char.IsAsciiDigit(character) && character is not ('+' or ' ' or '-' or '(' or ')')))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Count(character => character == '+') > 1 ||
            (trimmed.Contains('+') && trimmed[0] != '+')) return null;
        var number = new string(trimmed.Where(character => char.IsAsciiDigit(character) || character == '+').ToArray());
        return number.Count(char.IsAsciiDigit) >= 3 ? $"tel:{number}" : null;
    }

    private static string? SafeAbsoluteUrl(string? value, bool httpsOnly)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Any(char.IsControl) || trimmed.Contains('\\') || trimmed.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && (httpsOnly || uri.Scheme != Uri.UriSchemeHttp)) ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        return trimmed;
    }
}
