using DoggyDrop.Models;
using System.Globalization;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoggyDrop.ViewModels;

public sealed class PlaceInput
{
    public string? OriginalUpdatedAt { get; set; }

    public bool TryOriginalUpdatedAt(out DateTime version) =>
        DateTime.TryParseExact(OriginalUpdatedAt, "O", CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out version) && version.Kind == DateTimeKind.Utc;

    public bool IsFeatured { get; set; }
    public string? FeaturedFromLocal { get; set; }
    public string? FeaturedUntilLocal { get; set; }
    public string Name { get; set; } = string.Empty;
    public PlaceCategory Category { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? OpeningHours { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    [BindNever]
    public string? LogoUrl { get; set; }
    public IFormFile? LogoFile { get; set; }
    public bool RemoveLogo { get; set; }
    public int? DataSourceId { get; set; }
    public List<PlaceAmenityType> AmenityTypes { get; set; } = [];
    public string? AmenitiesSourceUrl { get; set; }
    public bool VerifyAmenitiesToday { get; set; }
    [BindNever]
    public DateTime? AmenitiesVerifiedAt { get; set; }

    public IEnumerable<(string Field, string Message)> Validate()
    {
        if (IsFeatured && !FeaturedPlaces.IsEligible(Category))
            yield return (nameof(IsFeatured), "Izpostavitev je na voljo samo za poslovne in storitvene lokacije.");
        var fromValid = FeaturedLocalTime.TryUtc(FeaturedFromLocal, out var from, out var fromError);
        var untilValid = FeaturedLocalTime.TryUtc(FeaturedUntilLocal, out var until, out var untilError);
        if (!fromValid) yield return (nameof(FeaturedFromLocal), fromError!);
        if (!untilValid) yield return (nameof(FeaturedUntilLocal), untilError!);
        if (fromValid && untilValid && from.HasValue && until.HasValue && from >= until)
            yield return (nameof(FeaturedUntilLocal), "Konec izpostavitve mora biti po začetku.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
            yield return (nameof(Name), "Vnesi ime lokacije (največ 120 znakov).");
        if (!PlaceCategories.IsSupported(Category))
            yield return (nameof(Category), "Izberi podprto kategorijo.");
        if (!Latitude.HasValue || !double.IsFinite(Latitude.Value) || Latitude.Value is < -90 or > 90)
            yield return (nameof(Latitude), "Izberi veljavno zemljepisno širino.");
        if (!Longitude.HasValue || !double.IsFinite(Longitude.Value) || Longitude.Value is < -180 or > 180)
            yield return (nameof(Longitude), "Izberi veljavno zemljepisno dolžino.");
        if (TooLong(Address, 180)) yield return (nameof(Address), "Naslov je predolg.");
        if (TooLong(Phone, 40)) yield return (nameof(Phone), "Telefonska številka je predolga.");
        if (TooLong(OpeningHours, 240)) yield return (nameof(OpeningHours), "Delovni čas je predolg.");
        if (TooLong(Description, 2000)) yield return (nameof(Description), "Opis je predolg.");
        if (TooLong(WebsiteUrl, 500) ||
            (!string.IsNullOrWhiteSpace(WebsiteUrl) && PlaceLinks.SafeWebsite(WebsiteUrl) == null))
            yield return (nameof(WebsiteUrl), "Vnesi veljavno povezavo HTTP ali HTTPS.");
        if (TooLong(ImageUrl, 500) ||
            (!string.IsNullOrWhiteSpace(ImageUrl) && PlaceLinks.SafeImage(ImageUrl) == null))
            yield return (nameof(ImageUrl), "Vnesi veljavno povezavo HTTPS do slike.");
        if ((AmenityTypes ?? []).Any(type => !PlaceAmenities.IsSupported(type)))
            yield return (nameof(AmenityTypes), "Izberi samo podprte ugodnosti za pse.");
        if (TooLong(AmenitiesSourceUrl, 500) ||
            (!string.IsNullOrWhiteSpace(AmenitiesSourceUrl) && PlaceLinks.SafeWebsite(AmenitiesSourceUrl) == null))
            yield return (nameof(AmenitiesSourceUrl), "Vnesi veljavno povezavo HTTP ali HTTPS do vira (največ 500 znakov).");
    }

    public void ApplyTo(Place place)
    {
        if (!FeaturedLocalTime.TryUtc(FeaturedFromLocal, out var from, out _) ||
            !FeaturedLocalTime.TryUtc(FeaturedUntilLocal, out var until, out _) ||
            (from.HasValue && until.HasValue && from >= until) || (IsFeatured && !FeaturedPlaces.IsEligible(Category)))
            throw new InvalidOperationException("Invalid Featured configuration.");
        place.IsFeatured = FeaturedPlaces.IsEligible(Category) && IsFeatured;
        place.FeaturedFrom = FeaturedPlaces.IsEligible(Category) ? from : null;
        place.FeaturedUntil = FeaturedPlaces.IsEligible(Category) ? until : null;
        place.Name = Name.Trim();
        place.Category = Category;
        place.Latitude = Latitude!.Value;
        place.Longitude = Longitude!.Value;
        place.Address = Clean(Address);
        place.Phone = Clean(Phone);
        place.WebsiteUrl = Clean(WebsiteUrl);
        place.OpeningHours = Clean(OpeningHours);
        place.Description = Clean(Description);
        place.ImageUrl = Clean(ImageUrl);
        place.DataSourceId = DataSourceId;

        // The tracked relationship diff is committed with the Place in one SaveChanges transaction.
        var selected = (AmenityTypes ?? []).ToHashSet();
        var source = Clean(AmenitiesSourceUrl);
        var changed = !selected.SetEquals(place.Amenities.Select(amenity => amenity.AmenityType)) ||
            source != place.AmenitiesSourceUrl;
        foreach (var removed in place.Amenities.Where(amenity => !selected.Contains(amenity.AmenityType)).ToList())
            place.Amenities.Remove(removed);
        var existing = place.Amenities.Select(amenity => amenity.AmenityType).ToHashSet();
        foreach (var type in selected.Where(type => !existing.Contains(type)))
            place.Amenities.Add(new PlaceAmenity { AmenityType = type });
        place.AmenitiesSourceUrl = source;
        // A previous verification does not certify newly edited facts or a different source.
        if (VerifyAmenitiesToday) place.AmenitiesVerifiedAt = DateTime.UtcNow;
        else if (changed) place.AmenitiesVerifiedAt = null;
    }

    public static PlaceInput FromPlace(Place place, string? cloudName) => new()
    {
        OriginalUpdatedAt = DateTime.SpecifyKind(place.UpdatedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        IsFeatured = place.IsFeatured, FeaturedFromLocal = FeaturedLocalTime.ForInput(place.FeaturedFrom),
        FeaturedUntilLocal = FeaturedLocalTime.ForInput(place.FeaturedUntil),
        Name = place.Name, Category = place.Category,
        Latitude = place.Latitude, Longitude = place.Longitude,
        Address = place.Address, Phone = place.Phone, WebsiteUrl = place.WebsiteUrl,
        OpeningHours = place.OpeningHours, Description = place.Description, ImageUrl = place.ImageUrl,
        LogoUrl = PlaceLogoDelivery.ForMarker(place.LogoUrl, cloudName),
        DataSourceId = place.DataSourceId,
        AmenityTypes = place.Amenities.Select(amenity => amenity.AmenityType).ToList(),
        AmenitiesSourceUrl = place.AmenitiesSourceUrl, AmenitiesVerifiedAt = place.AmenitiesVerifiedAt
    };

    private static bool TooLong(string? value, int max) => value?.Trim().Length > max;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PlaceMapItem(int Id, string Name, PlaceCategory Category,
    double Latitude, double Longitude, string? Address, string? LogoUrl,
    string CategoryLabel, string CategoryKey, string IconClass, bool IsCommercial, bool IsCurrentlyFeatured = false);

public sealed record PlaceDiscoveryItem(int Id, string Name, PlaceCategory Category,
    string CategoryLabel, string CategoryKey, string IconClass, bool IsCommercial,
    string? Address, double Latitude, double Longitude, string? LogoUrl, bool IsCurrentlyFeatured = false);

public sealed record PlaceDiscoveryViewModel(IReadOnlyList<PlaceDiscoveryItem> Places)
{
    public IReadOnlySet<int> SavedPlaceIds { get; init; } = new HashSet<int>();
}

public sealed record PlaceSaveViewModel(int PlaceId, string Name, bool IsSaved, string ReturnUrl);
public sealed record PlaceCardViewModel(PlaceDiscoveryItem Place, bool IsSaved, string ReturnUrl);

// Only fields needed by public Details; Admin verification metadata never enters this projection.
public sealed record PlaceDetailsData(
    int Id, string Name, PlaceCategory Category, double Latitude, double Longitude,
    string? Address, string? Phone, string? WebsiteUrl, string? OpeningHours,
    string? Description, string? ImageUrl, string? LogoUrl, List<PlaceAmenityType> AmenityTypes, bool IsCurrentlyFeatured = false);

public sealed record PlaceDetailsViewModel(
    int Id, string Name, PlaceCategory Category, string CategoryLabel, double Latitude, double Longitude,
    string? Address, string? Phone, string? TelephoneHref, string? WebsiteUrl,
    string? OpeningHours, string? Description, string? ImageUrl, string? LogoUrl,
    string CategoryKey, string IconClass, bool IsCommercial)
{
    public bool IsCurrentlyFeatured { get; init; }
    public bool IsSaved { get; init; }
    public IReadOnlyList<PlaceAmenityPresentation> Amenities { get; init; } = [];

    public static PlaceDetailsViewModel FromPublicData(PlaceDetailsData place, string? cloudName)
    {
        var category = PlaceCategories.Get(place.Category);
        return new PlaceDetailsViewModel(
            place.Id, place.Name, place.Category, category.Label, place.Latitude, place.Longitude,
            place.Address, place.Phone, PlaceLinks.TelephoneHref(place.Phone),
            PlaceLinks.SafeWebsite(place.WebsiteUrl), place.OpeningHours, place.Description,
            PlaceLinks.SafeImage(place.ImageUrl), PlaceCategories.PublicLogo(place.Category, place.LogoUrl, cloudName),
            category.Key, category.IconClass, category.IsCommercial)
        {
            IsCurrentlyFeatured = place.IsCurrentlyFeatured,
            Amenities = PlaceAmenities.Confirmed(place.AmenityTypes)
        };
    }
}
