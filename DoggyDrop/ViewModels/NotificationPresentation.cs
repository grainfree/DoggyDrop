using System.Globalization;
using DoggyDrop.Models;

namespace DoggyDrop.ViewModels;

public static class NotificationPresentation
{
    private static readonly CultureInfo Slovenian = CultureInfo.GetCultureInfo("sl-SI");

    public static string Title(UserNotification notification)
    {
        if (notification.Type == "BinApproved") return "Tvoj predlog koša je odobren";
        if (notification.Title == "First walk") return "Prvi sprehod";
        return TryCanonicalLevel(notification, out var level, out var rank)
            ? $"{level}. stopnja · {rank}"
            : notification.Title;
    }

    public static string Body(UserNotification notification)
    {
        if (TryCanonicalLevel(notification, out var level, out var rank) &&
            notification.Body == $"Dosegel si level {level} in naslov {rank}.")
        {
            return $"Dosegel si {level}. stopnjo in naziv {rank}.";
        }

        return notification.Body;
    }

    public static string Time(DateTime createdAtUtc, DateTime nowUtc)
    {
        var local = WalkMemoryPresentation.LocalTime(createdAtUtc);
        var today = WalkMemoryPresentation.LocalTime(nowUtc).Date;
        var time = local.ToString("HH:mm", Slovenian);
        if (local.Date == today) return $"danes · {time}";
        if (local.Date == today.AddDays(-1)) return $"včeraj · {time}";
        var date = local.Year == today.Year
            ? local.ToString("d. MMM", Slovenian)
            : local.ToString("d. MMM yyyy", Slovenian);
        return $"{date} · {time}";
    }

    private static bool TryCanonicalLevel(UserNotification notification, out int level, out string rank)
    {
        level = 0;
        rank = string.Empty;
        const string typePrefix = "LevelUp:";
        if (!notification.Type.StartsWith(typePrefix, StringComparison.Ordinal) ||
            !int.TryParse(notification.Type.AsSpan(typePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out level) ||
            level < 1)
        {
            return false;
        }

        var titlePrefix = $"Level {level}: ";
        if (!notification.Title.StartsWith(titlePrefix, StringComparison.Ordinal)) return false;
        rank = notification.Title[titlePrefix.Length..];
        return !string.IsNullOrWhiteSpace(rank);
    }
}
