namespace DoggyDrop.ViewModels
{
    public class AchievementItem
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public bool IsUnlocked { get; set; }

        public int ProgressPercent { get; set; }

        public string ProgressText { get; set; } = string.Empty;
    }
}
