namespace DoggyDrop.Services
{
    public enum GamificationStreakState { SafeToday, AtRiskToday, Expired }

    public class GamificationStreakInfo
    {
        public string StreakType { get; set; } = string.Empty;

        public string Label { get; set; } = string.Empty;

        public int StoredCurrentDays { get; set; }
        public int EffectiveCurrentDays { get; set; }
        public int CurrentDays => EffectiveCurrentDays;

        public int LongestDays { get; set; }

        public int FreezeCredits { get; set; }

        public DateOnly? LastActivityDate { get; set; }
        public GamificationStreakState State { get; set; }
        public bool IsSafeToday => State == GamificationStreakState.SafeToday;
        public bool IsAtRiskToday => State == GamificationStreakState.AtRiskToday;
        public string Guidance => State switch
        {
            GamificationStreakState.SafeToday => "Današnji niz je varen.",
            GamificationStreakState.AtRiskToday => "Danes opravi sprehod, da ohraniš niz.",
            _ => "Začni nov niz z današnjim sprehodom."
        };

        public string FlameTier
        {
            get
            {
                if (EffectiveCurrentDays >= 100) return "legendary";
                if (EffectiveCurrentDays >= 30) return "glowing";
                if (EffectiveCurrentDays >= 7) return "small";
                return "none";
            }
        }
    }
}
