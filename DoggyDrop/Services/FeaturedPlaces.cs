using System.Globalization;
using System.Linq.Expressions;
using DoggyDrop.Models;

namespace DoggyDrop.Services;

public sealed class FeaturedPlace
{
    public Place Place { get; init; } = null!;
    public bool IsCurrentlyFeatured { get; init; }
}

public static class FeaturedPlaces
{
    public static readonly PlaceCategory[] Eligible = PlaceCategories.All.Where(c => c.IsCommercial).Select(c => c.Category).ToArray();
    public static bool IsEligible(PlaceCategory category) => Eligible.Contains(category);

    // One rule shared by SQL projections and in-memory checks: [From, Until), in UTC.
    public static Expression<Func<Place, FeaturedPlace>> Projection(DateTime now) => place => new FeaturedPlace
    {
        Place = place,
        IsCurrentlyFeatured = place.IsFeatured && place.IsActive && Eligible.Contains(place.Category) &&
            (!place.FeaturedFrom.HasValue || place.FeaturedFrom <= now) &&
            (!place.FeaturedUntil.HasValue || place.FeaturedUntil > now)
    };
    public static IQueryable<FeaturedPlace> WithFeatured(this IQueryable<Place> places, DateTime now) => places.Select(Projection(now));
    public static bool IsCurrent(Place place, DateTime now) => Projection(now).Compile()(place).IsCurrentlyFeatured;
}

public static class FeaturedLocalTime
{
    private static readonly TimeZoneInfo Ljubljana = TimeZoneInfo.FindSystemTimeZoneById("Europe/Ljubljana");
    public static bool TryUtc(string? input, out DateTime? utc, out string? error)
    {
        utc = null; error = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (!DateTime.TryParseExact(input, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        { error = "Vnesi veljaven datum in čas."; return false; }
        var local = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        if (Ljubljana.IsInvalidTime(local))
        { error = "Ta čas ob prehodu na poletni čas ne obstaja. Izberi drug čas."; return false; }
        if (Ljubljana.IsAmbiguousTime(local))
        { error = "Ta čas se ob prehodu na zimski čas ponovi. Izberi čas zunaj podvojene ure."; return false; }
        utc = TimeZoneInfo.ConvertTimeToUtc(local, Ljubljana);
        return true;
    }
    public static string? ForInput(DateTime? utc) => utc.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc), Ljubljana)
            .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) : null;
}
