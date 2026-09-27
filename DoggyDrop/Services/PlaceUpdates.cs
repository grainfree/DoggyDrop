namespace DoggyDrop.Services;

public static class PlaceUpdates
{
    public static DateTime NextUpdatedAt(DateTime previous)
    {
        // Every Place writer must advance the existing concurrency token, including
        // origin/status-only bulk edits. PostgreSQL stores microsecond precision.
        const long ticksPerMicrosecond = 10;
        var now = DateTime.UtcNow.Ticks / ticksPerMicrosecond;
        var next = Math.Max(now, previous.Ticks / ticksPerMicrosecond + 1);
        return new DateTime(next * ticksPerMicrosecond, DateTimeKind.Utc);
    }
}
