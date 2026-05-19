namespace DoggyDrop.Services
{
    public class GamificationLevelInfo
    {
        public int TotalXp { get; set; }

        public int Level { get; set; }

        public string Title { get; set; } = "Puppy";

        public int CurrentLevelXp { get; set; }

        public int NextLevelXp { get; set; }

        public int XpIntoLevel => Math.Max(0, TotalXp - CurrentLevelXp);

        public int XpForNextLevel => Math.Max(1, NextLevelXp - CurrentLevelXp);

        public int XpRemaining => Math.Max(0, NextLevelXp - TotalXp);

        public int ProgressPercent => Math.Clamp((int)Math.Round(XpIntoLevel / (double)XpForNextLevel * 100), 0, 100);
    }
}
