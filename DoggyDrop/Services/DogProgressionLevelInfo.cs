namespace DoggyDrop.Services
{
    public class DogProgressionLevelInfo
    {
        public int TotalXp { get; set; }

        public int Level { get; set; } = 1;

        public int CurrentLevelXp { get; set; }

        public int NextLevelXp { get; set; } = 100;

        public int XpRemaining => Math.Max(0, NextLevelXp - TotalXp);

        public int ProgressPercent => Math.Clamp((int)Math.Round((TotalXp - CurrentLevelXp) / (double)Math.Max(1, NextLevelXp - CurrentLevelXp) * 100), 0, 100);
    }
}
