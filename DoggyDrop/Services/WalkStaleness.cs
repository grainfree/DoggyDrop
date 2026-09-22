using DoggyDrop.Models;

namespace DoggyDrop.Services;

public static class WalkStaleness
{
    public static readonly TimeSpan InactivityLimit = TimeSpan.FromHours(24);

    public static DateTime LastActivity(Walk walk, IEnumerable<WalkPoint> points, DateTime nowUtc)
    {
        var latestPoint = points
            .Where(point => point.RecordedAt >= walk.StartedAt && point.RecordedAt <= nowUtc)
            .Select(point => (DateTime?)point.RecordedAt)
            .Max();
        return latestPoint ?? walk.StartedAt;
    }

    public static bool IsStale(Walk walk, IEnumerable<WalkPoint> points, DateTime nowUtc) =>
        walk.Status == "Active" && nowUtc - LastActivity(walk, points, nowUtc) > InactivityLimit;
}
