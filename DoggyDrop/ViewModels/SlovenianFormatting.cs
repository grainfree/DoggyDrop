namespace DoggyDrop.ViewModels;

public static class SlovenianFormatting
{
    public static string Days(int days) => days == 1 ? "1 dan" : $"{days} dni";

    public static string PhotoCount(int count) => $"{count} {PhotoNoun(count)}";

    public static string PhotoNoun(int count) =>
        count % 100 is 11 or 12 or 13 or 14
            ? "fotografij"
            : (count % 10) switch
            {
                1 => "fotografija",
                2 => "fotografiji",
                3 or 4 => "fotografije",
                _ => "fotografij"
            };

    public static string WalkDistance(double meters)
    {
        if (!double.IsFinite(meters) || meters <= 0) return "0 m";
        if (meters < 10) return $"{Math.Max(1, (int)Math.Round(meters, MidpointRounding.AwayFromZero))} m";
        return $"{(meters / 1000d).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("sl-SI"))} km";
    }

    public static string WalkDuration(TimeSpan duration) => duration.TotalMinutes < 1
        ? $"{Math.Max(0, (int)duration.TotalSeconds)} s"
        : duration.TotalHours < 1
            ? $"{(int)duration.TotalMinutes} min"
            : $"{(int)duration.TotalHours} h {duration.Minutes} min";
}
