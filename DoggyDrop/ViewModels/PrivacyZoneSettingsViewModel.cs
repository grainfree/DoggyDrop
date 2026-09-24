namespace DoggyDrop.ViewModels;

public sealed class PrivacyZoneSettingsViewModel
{
    public bool IsEnabled { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int RadiusMeters { get; init; } = 300;
}

public sealed class PrivacyZoneSettingsInput
{
    public bool Enabled { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public int RadiusMeters { get; set; }
}
