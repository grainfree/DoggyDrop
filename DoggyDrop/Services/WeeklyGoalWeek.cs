namespace DoggyDrop.Services;

public readonly record struct WeeklyGoalWeek(DateOnly Monday, DateOnly NextMonday, DateTime StartUtc, DateTime EndUtc)
{
    private static readonly TimeZoneInfo Ljubljana = TimeZoneInfo.FindSystemTimeZoneById("Europe/Ljubljana");

    public static WeeklyGoalWeek At(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Utc ? utcInstant : DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Ljubljana));
        var daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7;
        var monday = localDate.AddDays(-daysSinceMonday);
        var nextMonday = monday.AddDays(7);
        return new WeeklyGoalWeek(monday, nextMonday, ToUtc(monday), ToUtc(nextMonday));
    }

    private static DateTime ToUtc(DateOnly day) => TimeZoneInfo.ConvertTimeToUtc(
        day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Ljubljana);
}
