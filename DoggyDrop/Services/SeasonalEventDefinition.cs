namespace DoggyDrop.Services
{
    public class SeasonalEventDefinition
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string RewardName { get; set; } = string.Empty;

        public string Theme { get; set; } = string.Empty;

        public DateOnly StartsOn { get; set; }

        public DateOnly EndsOn { get; set; }

        public string Metric { get; set; } = string.Empty;

        public int Target { get; set; }
    }
}
