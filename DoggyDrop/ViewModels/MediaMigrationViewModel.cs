namespace DoggyDrop.ViewModels
{
    public class MediaMigrationViewModel
    {
        public int CloudinaryCount { get; set; }

        public int R2Count { get; set; }

        public int R2OptimizationPendingCount { get; set; }

        public int LocalCount { get; set; }

        public int EmptyCount { get; set; }

        public int MigratedCount { get; set; }

        public int FailedCount { get; set; }

        public int MissingCount { get; set; }

        public int BatchLimit { get; set; } = 25;

        public string? Message { get; set; }

        public bool IsR2Configured { get; set; }

        public IReadOnlyList<MediaMigrationItemViewModel> PendingItems { get; set; } = [];

        public IReadOnlyList<MediaMigrationResultViewModel> Results { get; set; } = [];
    }

    public class MediaMigrationItemViewModel
    {
        public string SourceType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        public string? EntityKey { get; set; }

        public string Url { get; set; } = string.Empty;
    }

    public class MediaMigrationResultViewModel
    {
        public string SourceType { get; set; } = string.Empty;

        public int? EntityId { get; set; }

        public string? EntityKey { get; set; }

        public string OldUrl { get; set; } = string.Empty;

        public string? NewUrl { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? Error { get; set; }
    }
}
