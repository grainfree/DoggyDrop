using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DoggyDrop.ViewModels;

public sealed class PlaceInput
{
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

    public IEnumerable<(string Field, string Message)> Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
            yield return (nameof(Name), "Vnesi ime lokacije (največ 120 znakov).");
        if (!Enum.IsDefined(Category))
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
    }

    public void ApplyTo(Place place)
    {
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
    }

    public static PlaceInput FromPlace(Place place, string? cloudName) => new()
    {
        Name = place.Name, Category = place.Category,
        Latitude = place.Latitude, Longitude = place.Longitude,
        Address = place.Address, Phone = place.Phone, WebsiteUrl = place.WebsiteUrl,
        OpeningHours = place.OpeningHours, Description = place.Description, ImageUrl = place.ImageUrl,
        LogoUrl = PlaceLogoDelivery.ForMarker(place.LogoUrl, cloudName)
    };

    private static bool TooLong(string? value, int max) => value?.Trim().Length > max;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record PlaceMapItem(int Id, string Name, PlaceCategory Category,
    double Latitude, double Longitude, string? Address, string? LogoUrl);

public sealed record PlaceDetailsViewModel(
    int Id, string Name, PlaceCategory Category, string CategoryLabel, double Latitude, double Longitude,
    string? Address, string? Phone, string? TelephoneHref, string? WebsiteUrl,
    string? OpeningHours, string? Description, string? ImageUrl, string? LogoUrl)
{
    public static PlaceDetailsViewModel FromPlace(Place place, string? cloudName) => new(
        place.Id, place.Name, place.Category, PlaceCategories.Label(place.Category), place.Latitude, place.Longitude,
        place.Address, place.Phone, PlaceLinks.TelephoneHref(place.Phone),
        PlaceLinks.SafeWebsite(place.WebsiteUrl), place.OpeningHours, place.Description,
        PlaceLinks.SafeImage(place.ImageUrl), PlaceLogoDelivery.ForMarker(place.LogoUrl, cloudName));
}
