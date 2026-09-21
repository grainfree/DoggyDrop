namespace DoggyDrop.Services;

public interface IGamificationCalendar
{
    DateTimeOffset UtcNow { get; }
    DateOnly Today { get; }
    DateOnly ToLocalDate(DateTime utcInstant);
}
