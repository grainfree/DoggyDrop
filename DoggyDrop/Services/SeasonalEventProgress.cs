namespace DoggyDrop.Services
{
    public class SeasonalEventProgress
    {
        public string Key { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string RewardName { get; set; } = string.Empty;

        public string Theme { get; set; } = string.Empty;

        public DateOnly EndsOn { get; set; }

        public int Current { get; set; }

        public int Target { get; set; } = 1;

        public int ProgressPercent => Math.Clamp((int)Math.Round(Current / (double)Math.Max(1, Target) * 100), 0, 100);

        public bool IsComplete => Current >= Target;
    }
}
