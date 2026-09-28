using System.Globalization;
using System.Text;
using System.Text.Json;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public sealed record SeoMetadata(string Title, string Description, string CanonicalPath,
    PlaceDetailsViewModel? Place = null)
{
    public const string ViewDataKey = "SeoMetadata";
    public const string IndexableKey = "SeoIndexable";
    public const string RawHostKey = "SeoRawHost";
    public const string DefaultImagePath = "/images/icon-512.png";
    public const string ProjectPath = "/projekt";
    public const string MunicipalitiesPath = "/obcine";

    public static SeoMetadata Project { get; } = new("O projektu DoggyDrop",
        "Spoznaj DoggyDrop, brezplačno spletno aplikacijo za iskanje košev za pasje iztrebke in uporabnih pasjih lokacij.", ProjectPath);
    public static SeoMetadata Municipalities { get; } = new("DoggyDrop za občine",
        "Občine in komunalna podjetja lahko z obstoječimi podatki o lokacijah košev pomagajo dopolniti zemljevid DoggyDrop. Vključitev je brezplačna.", MunicipalitiesPath);

    public static SeoMetadata Home { get; } = new("DoggyDrop – zemljevid za sprehode s psom",
        "Najdi koše za pasje iztrebke in uporabne pasje lokacije ter beleži sprehode s psom.", "/");
    public static SeoMetadata Discovery { get; } = new("Pasje lokacije | DoggyDrop",
        "Razišči veterinarje, trgovine, pasje parke in druge uporabne lokacije za pse. Oglej si lokacijo in navodila za pot.", "/Places");

    public static SeoMetadata ForPlace(PlaceDetailsViewModel place) => new(
        $"{place.Name.Trim()} | DoggyDrop",
        $"{place.Name.Trim()} – {place.CategoryLabel}. Oglej si lokacijo in navodila za pot na DoggyDrop.",
        PlacePath(place.Id, place.Name), place);

    public static string PlacePath(int id, string name) => $"/lokacije/{id.ToString(CultureInfo.InvariantCulture)}/{Slug(name)}";
    public static string Slug(string? name)
    {
        var result = new StringBuilder();
        var separator = false;
        foreach (var c in (name ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (separator && result.Length > 0 && result.Length < 80) result.Append('-');
                if (result.Length == 80) break;
                result.Append(c); separator = false;
            }
            else separator = true;
        }
        var slug = result.ToString().TrimEnd('-');
        return slug.Length > 0 ? slug : "lokacija";
    }

    public string JsonLd(SeoSite site)
    {
        if (Place is not { } p) return "";
        // Conservative types, verified against schema.org. VeterinaryCare is an
        // Organization, so its public coordinates belong on a nested location.
        var type = p.Category switch {
            PlaceCategory.Veterinarian => "VeterinaryCare",
            PlaceCategory.PetShop => "PetStore",
            PlaceCategory.Groomer or PlaceCategory.DogSchool => "LocalBusiness",
            PlaceCategory.DogFriendlyCafe => "CafeOrCoffeeShop",
            PlaceCategory.DogPark => "Park",
            _ => "Place"
        };
        var geo = new Dictionary<string, object> { ["@type"] = "GeoCoordinates", ["latitude"] = p.Latitude, ["longitude"] = p.Longitude };
        var data = new Dictionary<string, object> {
            ["@context"] = "https://schema.org", ["@type"] = type,
            ["name"] = p.Name.Trim(), ["url"] = site.Absolute(CanonicalPath)
        };
        if (type == "VeterinaryCare") data["location"] = new Dictionary<string, object> { ["@type"] = "Place", ["geo"] = geo };
        else data["geo"] = geo;
        if (!string.IsNullOrWhiteSpace(p.Address)) data["address"] = p.Address;
        if (p.TelephoneHref != null) data["telephone"] = p.Phone!;
        // Arbitrary external photos are not fetched or assumed available. Use only
        // the existing validated managed public logo as an optional Place image.
        if (PublicLogo(p.LogoUrl) is { } logo) data["image"] = logo;
        // Default System.Text.Json escaping protects script boundaries and HTML.
        return JsonSerializer.Serialize(data);
    }

    public string SocialImage(SeoSite site) => PublicLogo(Place?.LogoUrl) ?? site.Absolute(DefaultImagePath);
    private static string? PublicLogo(string? logo) =>
        logo != null && Uri.TryCreate(logo, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Host == "res.cloudinary.com" && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsolutePath.Contains("/image/upload/c_fit,w_128,h_128/f_auto,q_auto/v", StringComparison.Ordinal)
            ? logo : null;
}

public sealed class SeoSite
{
    public Uri Origin { get; }
    private readonly bool enabled;
    public SeoSite(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Seo:PublicOrigin"] ?? "https://doggydrop.app";
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
            origin.HostNameType != UriHostNameType.Dns || !origin.IsDefaultPort || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) || !string.IsNullOrEmpty(origin.UserInfo))
            throw new InvalidOperationException("Seo:PublicOrigin must be an HTTPS origin without a path, query or credentials.");
        Origin = origin;
        enabled = environment.IsProduction() && configuration.GetValue("Seo:AllowIndexing", true) &&
            !string.Equals(configuration["IS_PULL_REQUEST"], "true", StringComparison.OrdinalIgnoreCase);
    }
    public string Absolute(string path)
    {
        if (!path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\') || path.Any(char.IsControl))
            throw new ArgumentException("Expected a local absolute path.", nameof(path));
        return Origin.GetLeftPart(UriPartial.Authority) + path;
    }
    public bool AllowsIndexing(HttpContext context)
    {
        var host = context.Items[SeoMetadata.RawHostKey] as string ?? context.Request.Host.Value;
        return enabled && (string.Equals(host, Origin.Host, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, Origin.Host + ":443", StringComparison.OrdinalIgnoreCase));
    }
}
