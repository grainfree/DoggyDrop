namespace DoggyDrop.Services
{
    public class GamificationStreakInfo
    {
        public string StreakType { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int CurrentDays { get; set; }

        public int LongestDays { get; set; }

        public int FreezeCredits { get; set; }

        public DateOnly? LastActivityDate { get; set; }

        public string FlameTier
        {
            get
            {
                if (CurrentDays >= 100) return "legendary";
                if (CurrentDays >= 30) return "glowing";
                if (CurrentDays >= 7) return "small";
                return "none";
            }
        }
    }
}
