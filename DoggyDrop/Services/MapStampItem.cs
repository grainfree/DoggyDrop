namespace DoggyDrop.Services
{
    public class MapStampItem
    {
        public string PlaceKey { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Area { get; set; }

        public string Rarity { get; set; } = "Common";

        public int VisitCount { get; set; }

        public DateTime FirstCollectedAt { get; set; }

        public DateTime LastCollectedAt { get; set; }
    }
}
