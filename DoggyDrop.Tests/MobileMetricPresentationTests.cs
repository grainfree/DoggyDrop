using Xunit;

namespace DoggyDrop.Tests;

public sealed class MobileMetricPresentationTests
{
    [Fact]
    public void Profile_HasLabelledSettingsAndDistinctStreakIcons()
    {
        var view = ReadSource("DoggyDrop", "Views", "Home", "UserProfile.cshtml");

        Assert.Contains("asp-action=\"Settings\"><i class=\"bi bi-gear\" aria-hidden=\"true\"></i> Nastavitve", view);
        Assert.Contains("isWalkStreak ? \"bi-fire\" : \"bi-compass\"", view);
        Assert.Contains("profile-streak--inactive", view);
        Assert.DoesNotContain("profile-streak__flame", view);
        Assert.Contains("profile-stats dd-metric-grid", view);
    }

    [Fact]
    public void Walks_SeparatesCurrentProgressFromLifetimeStatistics()
    {
        var view = ReadSource("DoggyDrop", "Views", "Walks", "Index.cshtml");
        var hero = Between(view, "<div class=\"walks-hero__pulse", "</header>");
        var statistics = Between(view, "<section class=\"walk-statistics\"", "<section class=\"walk-week-card\">");

        Assert.Contains("Model.ActivityInsights.WeeklyDistanceKm", hero);
        Assert.DoesNotContain("Model.TotalDistanceKm", hero);
        Assert.Contains("Model.TotalDistanceKm", statistics);
        Assert.DoesNotContain("Model.WalksThisWeek", statistics);
        Assert.Contains("Statistika", statistics);
        Assert.Contains("dd-metric-grid", hero);
        Assert.Contains("dd-metric-grid", statistics);
    }

    [Fact]
    public void Community_UsesSharedMetricsWithoutChangingLeaderboard()
    {
        var view = ReadSource("DoggyDrop", "Views", "Home", "Community.cshtml");

        Assert.Contains("community-stats dd-metric-grid", view);
        Assert.Contains("community-local-grid", view);
        Assert.Contains("Model.LocalLeaderboards.MostDistance", view);
    }

    [Fact]
    public void SharedMetrics_KeepMobileGridAndBottomNavigationClearance()
    {
        var css = ReadSource("DoggyDrop", "wwwroot", "css", "site.css");

        Assert.Contains(".app-page .dd-metric .dd-metric__value", css);
        Assert.Contains(".app-page .dd-metric .dd-metric__label", css);
        Assert.Contains(".app-page .dd-metric-grid.dd-metric-grid > .dd-metric:first-child", css);
        Assert.Contains(".app-page:is(.profile-page, .walks-page, .community-page) { padding-bottom: var(--app-bottom-nav-clearance); }", css);
    }

    private static string Between(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Missing end marker: {end}");
        return source[startIndex..endIndex];
    }

    private static string ReadSource(params string[] pathParts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. pathParts]));
    }
}
