namespace DoggyDrop.ViewModels;

public static class SlovenianFormatting
{
    public static string Days(int days) => days == 1 ? "1 dan" : $"{days} dni";

    public static string WalkDistance(double meters) =>
        $"{(meters / 1000d).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("sl-SI"))} km";

    public static string WalkDuration(TimeSpan duration) => duration.TotalMinutes < 1
        ? $"{Math.Max(0, (int)duration.TotalSeconds)} s"
        : duration.TotalHours < 1
            ? $"{(int)duration.TotalMinutes} min"
            : $"{(int)duration.TotalHours} h {duration.Minutes} min";
}
