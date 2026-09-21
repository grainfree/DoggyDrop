namespace DoggyDrop.Services;

public sealed class GamificationCalendar : IGamificationCalendar
{
    private static readonly TimeZoneInfo DoggyDropTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Ljubljana");
    private readonly TimeProvider _timeProvider;

    public GamificationCalendar(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateOnly Today => ToLocalDate(UtcNow.UtcDateTime);

    public DateOnly ToLocalDate(DateTime utcInstant)
    {
        var normalizedUtc = utcInstant.Kind == DateTimeKind.Utc ? utcInstant : DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, DoggyDropTimeZone));
    }
}
