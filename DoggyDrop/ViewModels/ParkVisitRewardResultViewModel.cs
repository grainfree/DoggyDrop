namespace DoggyDrop.ViewModels;

public sealed class ParkVisitRewardResultViewModel
{
    public bool IsNewUserDiscovery { get; set; }
    public bool IsNewForDog { get; set; }
    public string ParkName { get; set; } = string.Empty;
    public int VisitCount { get; set; }
    public GamificationRewardResultViewModel Progression { get; set; } = new();
    public MapStampRewardViewModel? Stamp { get; set; }
}

public sealed class MapStampRewardViewModel
{
    public string LocationName { get; set; } = string.Empty;
    public string Rarity { get; set; } = "Common";
    public string? PreviousRarity { get; set; }
    public bool IsNew { get; set; }
    public bool WasUpgraded { get; set; }
}
