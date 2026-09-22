using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public sealed record UserAchievementProgress(
    int DogCount,
    int CompletedWalkCount,
    double CompletedWalkDistanceKm,
    int BinSubmissionCount,
    int UniquePlaceCount);

public static class UserAchievementPresentationBuilder
{
    public static IReadOnlyList<AchievementItem> Build(
        UserAchievementProgress progress,
        IReadOnlyList<UserAchievement> ownedAchievements,
        string? category = null)
    {
        var ownedByKey = ownedAchievements.ToDictionary(item => item.AchievementKey, StringComparer.Ordinal);
        return UserAchievementCatalog.All
            .Where(definition => category == null || definition.Category == category)
            .Select(definition =>
        {
            var current = definition.Key switch
            {
                UserAchievementCatalog.DogParent => progress.DogCount,
                UserAchievementCatalog.WalkFirst => progress.CompletedWalkCount,
                UserAchievementCatalog.Walk10Km or UserAchievementCatalog.Walk100Km => progress.CompletedWalkDistanceKm,
                UserAchievementCatalog.BinFirstSubmission or UserAchievementCatalog.Bin10Submissions => progress.BinSubmissionCount,
                UserAchievementCatalog.Explorer5Places => progress.UniquePlaceCount,
                _ => 0
            };
            ownedByKey.TryGetValue(definition.Key, out var owned);
            var currentText = definition.ProgressUnit == "km" ? current.ToString("0.0") : Math.Floor(current).ToString("0");
            return new AchievementItem
            {
                Key = definition.Key,
                Name = definition.DisplayName,
                Description = definition.Description,
                IsUnlocked = owned != null,
                UnlockedAt = owned?.UnlockedAt,
                ProgressPercent = owned != null ? 100 : Math.Clamp((int)Math.Round(current / definition.ProgressTarget * 100), 0, 100),
                ProgressText = owned != null ? "Odklenjeno" : $"{currentText} / {definition.ProgressTarget:0} {definition.ProgressUnit}"
            };
        }).ToList();
    }
}
