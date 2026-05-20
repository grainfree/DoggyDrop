using DoggyDrop.Models;

namespace DoggyDrop.ViewModels
{
    public class WalkPlannerViewModel
    {
        public IReadOnlyList<Dog> Dogs { get; set; } = [];

        public int? SelectedDogId { get; set; }

        public string AreaKey { get; set; } = "maribor";

        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public bool UsesCurrentLocation { get; set; }

        public string StartLabel { get; set; } = "Moja lokacija";

        public double TargetDistanceKm { get; set; } = 3;

        public bool IncludeBins { get; set; } = true;

        public bool IncludePark { get; set; } = true;

        public bool IncludeWater { get; set; } = true;

        public bool IncludeDogFriendly { get; set; } = true;

        public string WalkStyle { get; set; } = "balanced";

        public string DogEnergy { get; set; } = "auto";

        public PlannedWalkRoute? Route { get; set; }

        public IReadOnlyList<WalkPlannerArea> Areas { get; set; } = [];

        public IReadOnlyList<PlannerStyleOption> Styles { get; set; } = [];

        public IReadOnlyList<QuickWalkTemplate> Presets { get; set; } = [];
    }

    public class WalkPlannerArea
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    public class PlannedWalkRoute
    {
        public string Title { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public double TargetDistanceKm { get; set; }

        public double EstimatedDistanceKm { get; set; }

        public int EstimatedMinutes { get; set; }

        public IReadOnlyList<PlannedWalkRouteStop> Stops { get; set; } = [];

        public IReadOnlyList<PlannedWalkPoint> RoutePoints { get; set; } = [];

        public int? SavedPlanId { get; set; }
    }

    public class PlannedWalkRouteStop
    {
        public string Name { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public int Order { get; set; }
    }

    public class PlannedWalkPoint
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }

    public class PlannedWalkSummaryItem
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? DogName { get; set; }

        public int? DogId { get; set; }

        public string AreaName { get; set; } = string.Empty;

        public double TargetDistanceKm { get; set; }

        public int EstimatedMinutes { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UsedAt { get; set; }
    }

    public class PlannerStyleOption
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }
}
