using DoggyDrop.Models;

namespace DoggyDrop.Services;

public sealed record PlaceCategoryPresentation(
    PlaceCategory Category, string Label, string FilterLabel, string Key, string IconClass, bool IsCommercial);

public static class PlaceCategories
{
    public static IReadOnlyList<PlaceCategoryPresentation> All { get; } =
    [
        new(PlaceCategory.Veterinarian, "Veterinar", "Veterinarji", "veterinarian", "dd-place-icon--veterinarian", true),
        new(PlaceCategory.PetShop, "Trgovina", "Trgovine", "pet-shop", "dd-place-icon--pet-shop", true),
        new(PlaceCategory.Groomer, "Pasji salon", "Saloni", "groomer", "dd-place-icon--groomer", true),
        new(PlaceCategory.DogSchool, "Pasja šola", "Pasje šole", "dog-school", "dd-place-icon--dog-school", true),
        new(PlaceCategory.DogFriendlyCafe, "Psom prijazen lokal", "Lokali", "dog-friendly-cafe", "dd-place-icon--dog-friendly-cafe", true),
        new(PlaceCategory.DogPark, "Pasji park", "Pasji parki", "dog-park", "dd-place-icon--dog-park", false),
        new(PlaceCategory.DogBeach, "Pasja plaža", "Pasje plaže", "dog-beach", "dd-place-icon--dog-beach", false)
    ];

    public static IReadOnlyList<PlaceCategory> Supported { get; } =
        All.Select(item => item.Category).ToArray();

    public static bool IsSupported(PlaceCategory category) => Supported.Contains(category);

    private static readonly PlaceCategoryPresentation Unknown =
        new((PlaceCategory)0, "Lokacija", "Lokacije", "other", "dd-place-icon--other", false);

    public static PlaceCategoryPresentation Get(PlaceCategory category) =>
        All.FirstOrDefault(item => item.Category == category) ?? Unknown;

    public static string Label(PlaceCategory category) => Get(category).Label;
    public static string DiscoveryFilterLabel(PlaceCategory category) => Get(category).FilterLabel;
    // Public dog destinations retain their category identity even when an admin has uploaded a logo.
    public static string? PublicLogo(PlaceCategory category, string? logoUrl, string? cloudName) =>
        Get(category).IsCommercial ? PlaceLogoDelivery.ForMarker(logoUrl, cloudName) : null;
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
