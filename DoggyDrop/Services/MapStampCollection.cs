namespace DoggyDrop.Services
{
    public class MapStampCollection
    {
        public int TotalStamps { get; set; }

        public int CommonCount { get; set; }

        public int RareCount { get; set; }

        public int EpicCount { get; set; }

        public int LegendaryCount { get; set; }

        public IReadOnlyList<MapStampItem> Stamps { get; set; } = [];
    }
}
