using DoggyDrop.Models;

namespace DoggyDrop.ViewModels;

public sealed class DogAdventuresViewModel
{
    public Dog Dog { get; init; } = null!;
    public IReadOnlyList<DogAdventureMonthViewModel> Months { get; init; } = [];
    public int Page { get; init; }
    public bool HasNext { get; init; }
    public int? ActiveWalkId { get; init; }
}

public sealed class DogAdventureMonthViewModel
{
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<DogAdventureItemViewModel> Walks { get; init; } = [];
}

public sealed class DogAdventureItemViewModel
{
    public int WalkId { get; init; }
    public DateTime StartedAt { get; init; }
    public double DistanceMeters { get; init; }
    public int PhotoCount { get; init; }
    public string? HeroPhotoUrl { get; init; }
    public string DateLabel => WalkMemoryPresentation.LocalTime(StartedAt)
        .ToString("d. MMM · HH:mm", System.Globalization.CultureInfo.GetCultureInfo("sl-SI"));
    public string DistanceLabel => SlovenianFormatting.WalkDistance(DistanceMeters);
}
