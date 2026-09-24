namespace DoggyDrop.ViewModels;

public sealed class NearbyDiscoverySettingsViewModel
{
    public bool IsEnabled { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int RadiusMeters { get; init; } = 3000;
    public bool BinsEnabled { get; init; } = true;
}

public sealed class NearbyDiscoverySettingsInput
{
    public bool Enabled { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public int RadiusMeters { get; set; }
    public bool BinsEnabled { get; set; }
}
