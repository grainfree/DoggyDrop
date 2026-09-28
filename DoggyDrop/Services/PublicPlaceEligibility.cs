using DoggyDrop.Models;

namespace DoggyDrop.Services;

public static class PublicPlaceEligibility
{
    // Apply before projection: finite bounds also exclude PostgreSQL NaN/infinities.
    public static IQueryable<Place> ForPublicDetails(this IQueryable<Place> places) => places.Where(p =>
        p.IsActive && PlaceCategories.Supported.Contains(p.Category) &&
        p.Name != null && p.Name.Trim() != "" && p.Name.Trim().Length <= 120 &&
        p.Latitude >= -90 && p.Latitude <= 90 && p.Longitude >= -180 && p.Longitude <= 180);

    // Unicode/control validation after the minimal SQL projection is identical for
    // Details and sitemap; no attempt to invent or repair corrupt stored facts.
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= 120 && !name.Any(char.IsControl);
}
