namespace DoggyDrop.ViewModels
{
    public class CityDashboardViewModel
    {
        public int TotalBins { get; set; }

        public int ApprovedBins { get; set; }

        public int PendingBins { get; set; }

        public int FullReports { get; set; }

        public int MissingReports { get; set; }

        public int BinUses { get; set; }

        public int CompletedWalks { get; set; }

        public double TotalWalkKm { get; set; }

        public int ActiveDogs { get; set; }

        public int OpenPlaydates { get; set; }

        public IReadOnlyList<CityBinIssueItem> BinIssues { get; set; } = [];

        public IReadOnlyList<CityTopBinItem> TopBins { get; set; } = [];

        public IReadOnlyList<CityWeeklyActivityItem> WeeklyActivity { get; set; } = [];
    }

    public class CityBinIssueItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int FullReports { get; set; }

        public int MissingReports { get; set; }

        public DateTime? LastReportedAt { get; set; }
    }

    public class CityTopBinItem
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int UsedCount { get; set; }

        public int UsefulVotes { get; set; }
    }

    public class CityWeeklyActivityItem
    {
        public string DayLabel { get; set; } = string.Empty;

        public int WalkCount { get; set; }

        public double DistanceKm { get; set; }

        public int IntensityPercent { get; set; }
    }
}
