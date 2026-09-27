namespace DoggyDrop.Models;

public enum PlaceAmenityType
{
    DogsInside = 1,
    DogsTerrace = 2,
    WaterForDogs = 3,
    Fenced = 4,
    OffLeash = 5,
    DogsInWater = 6,
    DogShower = 7,
    WasteBins = 8
}

// A row is an explicitly confirmed positive fact. Absence means unknown.
public sealed class PlaceAmenity
{
    public int PlaceId { get; set; }
    public PlaceAmenityType AmenityType { get; set; }
    public Place Place { get; set; } = null!;
}
