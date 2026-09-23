using DoggyDrop.Models;
using DoggyDrop.Services;

namespace DoggyDrop.ViewModels;

public sealed class WalkMemoryViewModel
{
    public string Title { get; init; } = string.Empty;
    public string DateLabel { get; init; } = string.Empty;
    public string DistanceLabel { get; init; } = string.Empty;
    public string? DurationLabel { get; init; }
    public string? OwnerPlanTitle { get; init; }
    public string? HeroPhotoUrl { get; init; }
    public int PhotoCount { get; init; }
    public bool HasActualTrail { get; init; }
    public bool HasPlannedRoute { get; init; }
    public bool HasMap => HasActualTrail || HasPlannedRoute;
    public string ShareText { get; init; } = string.Empty;
    public WalkShareAssetViewModel? ShareAsset { get; init; }
    public IReadOnlyList<WalkMemoryHighlight> Highlights { get; init; } = [];
}

public sealed record WalkMemoryHighlight(string Title, string Detail);

public sealed class WalkShareAssetViewModel
{
    public string DogName { get; init; } = string.Empty;
    public string Distance { get; init; } = string.Empty;
    public string? Duration { get; init; }
    public string Date { get; init; } = string.Empty;
    public string? Highlight { get; init; }
    public string? PhotoUrl { get; init; }
    public string Text { get; init; } = string.Empty;
}

public static class WalkMemoryPresentation
{
    private static readonly TimeZoneInfo Ljubljana = TimeZoneInfo.FindSystemTimeZoneById("Europe/Ljubljana");
    private static readonly System.Globalization.CultureInfo Slovenian = System.Globalization.CultureInfo.GetCultureInfo("sl-SI");

    public static DateTime LocalTime(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Utc ? utcInstant : DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Ljubljana);
    }

    public static WalkMemoryViewModel Build(
        Walk walk,
        IReadOnlyList<UserXpEvent> userXpEvents,
        IReadOnlyList<DogXpEvent> dogXpEvents,
        IReadOnlyList<UserAchievement> achievements,
        bool includeOwnerDetails = false)
    {
        var title = $"Sprehod s {walk.Dog?.Name ?? "psom"}";
        var date = LocalTime(walk.StartedAt).ToString("d. MMMM yyyy · HH:mm", Slovenian);
        var distance = SlovenianFormatting.WalkDistance(walk.DistanceMeters);
        var duration = HasCredibleElapsedDuration(walk)
            ? SlovenianFormatting.WalkDuration(walk.EndedAt!.Value - walk.StartedAt)
            : null;
        var photos = (walk.Photos ?? []).Where(photo => photo.WalkId == walk.Id && photo.UserId == walk.OwnerId && !string.IsNullOrWhiteSpace(photo.ImageUrl))
            .OrderByDescending(photo => photo.CreatedAt).ThenByDescending(photo => photo.Id).ToList();
        var hasActualTrail = HasUsefulLine((walk.Points ?? []).Select(point => (point.Latitude, point.Longitude)));
        var hasPlannedRoute = HasUsefulLine((walk.PlannedWalk?.RoutePoints ?? []).Select(point => (point.Latitude, point.Longitude)));
        var highlights = new List<WalkMemoryHighlight>();

        var userXp = userXpEvents.Where(item => item.ActivityType == GamificationConstants.WalkDistance).Sum(item => item.XpAmount);
        if (userXp > 0) highlights.Add(new WalkMemoryHighlight("Tvoje izkušnje", $"+{userXp} XP"));
        var dogXp = dogXpEvents.Where(item => item.ActivityType == "CompletedWalk").Sum(item => item.XpAmount);
        if (dogXp > 0) highlights.Add(new WalkMemoryHighlight("Pasji napredek", $"+{dogXp} XP"));
        foreach (var achievement in achievements.OrderBy(item => item.UnlockedAt))
        {
            var definition = UserAchievementCatalog.All.FirstOrDefault(item => item.Key == achievement.AchievementKey);
            if (definition != null) highlights.Add(new WalkMemoryHighlight("Odklenjen dosežek", definition.DisplayName));
        }
        var completedStops = (walk.StopCompletions ?? []).Count(item => item.PlannedWalkStop != null);
        if (completedStops > 0) highlights.Add(new WalkMemoryHighlight("Potrjeni postanki", completedStops.ToString(Slovenian)));

        var shareText = $"{walk.Dog?.Name ?? "Pes"} · {distance}. Dogodivščina z DoggyDrop.";
        var shareHighlight = highlights.FirstOrDefault(item => item.Title == "Odklenjen dosežek")?.Detail
            ?? (userXp > 0 ? $"+{userXp} XP" : null);
        return new WalkMemoryViewModel
        {
            Title = title,
            DateLabel = date,
            DistanceLabel = distance,
            DurationLabel = duration,
            OwnerPlanTitle = includeOwnerDetails ? walk.PlannedWalk?.Title : null,
            HeroPhotoUrl = photos.FirstOrDefault()?.ImageUrl,
            PhotoCount = photos.Count,
            HasActualTrail = hasActualTrail,
            HasPlannedRoute = hasPlannedRoute,
            ShareText = shareText,
            ShareAsset = includeOwnerDetails ? new WalkShareAssetViewModel
            {
                DogName = walk.Dog?.Name ?? "Pes",
                Distance = distance,
                Duration = duration,
                Date = LocalTime(walk.StartedAt).ToString("dd.MM.yyyy", Slovenian),
                Highlight = shareHighlight,
                PhotoUrl = photos.FirstOrDefault()?.ImageUrl,
                Text = shareText
            } : null,
            Highlights = highlights
        };
    }

    private static bool HasUsefulLine(IEnumerable<(double Latitude, double Longitude)> points) =>
        points.Where(point => double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude)
                && Math.Abs(point.Latitude) <= 90 && Math.Abs(point.Longitude) <= 180)
            .Distinct().Take(2).Count() == 2;

    private static bool HasCredibleElapsedDuration(Walk walk)
    {
        if (walk.EndedAt is not { } endedAt) return false;
        var elapsed = endedAt - walk.StartedAt;
        if (elapsed <= TimeSpan.Zero) return false;

        var points = (walk.Points ?? [])
            .Where(point => point.RecordedAt >= walk.StartedAt && point.RecordedAt <= endedAt &&
                double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude) &&
                Math.Abs(point.Latitude) <= 90 && Math.Abs(point.Longitude) <= 180)
            .OrderBy(point => point.RecordedAt)
            .ToList();
        if (points.Count < 2 || !HasUsefulLine(points.Select(point => (point.Latitude, point.Longitude)))) return false;

        // Elapsed time is shown only when GPS covers both ends without a conspicuous gap.
        // It is never presented as measured active walking time.
        var edgeTolerance = TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(30).Ticks,
            Math.Max(TimeSpan.FromMinutes(5).Ticks, elapsed.Ticks / 10)));
        if (points[0].RecordedAt - walk.StartedAt > edgeTolerance || endedAt - points[^1].RecordedAt > edgeTolerance)
            return false;
        var gapTolerance = TimeSpan.FromTicks(Math.Min(WalkStaleness.InactivityLimit.Ticks,
            Math.Max(TimeSpan.FromHours(1).Ticks, elapsed.Ticks / 4)));
        return !points.Zip(points.Skip(1), (first, second) => second.RecordedAt - first.RecordedAt)
            .Any(gap => gap > gapTolerance);
    }
}
