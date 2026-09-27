using DoggyDrop.Models;

namespace DoggyDrop.Services;

public sealed record PlaceAmenityPresentation(PlaceAmenityType Type, string Label, string IconClass, string Group);

public static class PlaceAmenities
{
    public static IReadOnlyList<PlaceAmenityPresentation> All { get; } =
    [
        new(PlaceAmenityType.DogsInside, "Psi dobrodošli v notranjosti", "bi-door-open", "Dostop"),
        new(PlaceAmenityType.DogsTerrace, "Psi dobrodošli na terasi", "bi-umbrella", "Dostop"),
        new(PlaceAmenityType.WaterForDogs, "Voda za pse", "bi-droplet", "Na lokaciji"),
        new(PlaceAmenityType.Fenced, "Ograjeno", "bi-bounding-box", "Prostor"),
        new(PlaceAmenityType.OffLeash, "Prosto gibanje brez povodca", "bi-arrows-move", "Prostor"),
        new(PlaceAmenityType.DogsInWater, "Kopanje psov dovoljeno", "bi-water", "Prostor"),
        new(PlaceAmenityType.DogShower, "Tuš za pse", "bi-cloud-drizzle", "Na lokaciji"),
        new(PlaceAmenityType.WasteBins, "Koš za pasje iztrebke", "bi-trash", "Na lokaciji")
    ];

    public static bool IsSupported(PlaceAmenityType type) => All.Any(item => item.Type == type);

    public static IReadOnlyList<PlaceAmenityPresentation> Confirmed(IEnumerable<PlaceAmenityType> types)
    {
        var confirmed = types.ToHashSet();
        // Iterate the authoritative list so unknown persisted values are never public claims.
        return All.Where(item => confirmed.Contains(item.Type)).ToList();
    }
}
