namespace DoggyDrop.Services;

public sealed class AchievementUnlockResult
{
    public string AchievementKey { get; init; } = string.Empty;
    public bool NewlyUnlocked { get; init; }
    public DateTime UnlockedAt { get; init; }
    public string? SourceType { get; init; }
    public string? SourceId { get; init; }
}
