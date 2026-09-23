using System.Globalization;

namespace DoggyDrop.ViewModels;

public sealed record WeeklyGoalItem(string Key, string Title, string Description, string Icon, double Current, double Target, string ProgressLabel, string RemainingLabel)
{
    public bool IsComplete => double.IsFinite(Current) && double.IsFinite(Target) && Target > 0 && Current >= Target;
    public int ProgressPercent => IsComplete ? 100
        : !double.IsFinite(Current) || !double.IsFinite(Target) || Target <= 0 ? 0
        : (int)Math.Clamp(Math.Floor(Current / Target * 100), 0, 99);
}

public sealed class WeeklyGoalsViewModel
{
    public DateOnly Monday { get; init; }
    public DateOnly Sunday => Monday.AddDays(6);
    public IReadOnlyList<WeeklyGoalItem> Goals { get; init; } = [];
    public int CompletedCount => Goals.Count(goal => goal.IsComplete);
    public bool HasActivity => Goals.Any(goal => goal.Current > 0);
    public string CompletedSummary => $"Doseženi cilji: {CompletedCount}/{Goals.Count}";
    public bool AllComplete => Goals.Count > 0 && CompletedCount == Goals.Count;
    public WeeklyGoalItem? NextGoal => Goals.Where(goal => !goal.IsComplete)
        .OrderBy(goal => (goal.Target - goal.Current) / goal.Target)
        .ThenBy(goal => Array.IndexOf(new[] { "walks", "distance", "photo" }, goal.Key))
        .FirstOrDefault();

    public string DateLabel => Monday.Month == Sunday.Month
        ? $"{Monday.Day}.–{Sunday.Day}. {Month(Sunday)}"
        : $"{Monday.Day}. {Month(Monday)}–{Sunday.Day}. {Month(Sunday)}";

    private static string Month(DateOnly date) => date.ToString("MMMM", CultureInfo.GetCultureInfo("sl-SI"));
}

public static class WeeklyGoalsWording
{
    public static string Walks(int count)
    {
        var lastTwo = count % 100;
        var form = lastTwo is >= 11 and <= 14 ? "sprehodov" : (count % 10) switch
        {
            1 => "sprehod",
            2 => "sprehoda",
            3 or 4 => "sprehodi",
            _ => "sprehodov"
        };
        return $"{count} {form}";
    }
}
