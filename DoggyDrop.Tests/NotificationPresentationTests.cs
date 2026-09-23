using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class NotificationPresentationTests
{
    [Fact]
    public void CanonicalLevelUpIsLocalizedWithoutChangingStoredValues()
    {
        var notification = new UserNotification
        {
            Type = "LevelUp:3",
            Title = "Level 3: Puppy",
            Body = "Dosegel si level 3 in naslov Puppy."
        };

        Assert.Equal("3. stopnja · Puppy", NotificationPresentation.Title(notification));
        Assert.Equal("Dosegel si 3. stopnjo in naziv Puppy.", NotificationPresentation.Body(notification));
        Assert.Equal("Level 3: Puppy", notification.Title);
        Assert.Equal("Dosegel si level 3 in naslov Puppy.", notification.Body);
    }

    [Fact]
    public void UnknownOrMismatchedLevelTextIsUnchanged()
    {
        var custom = new UserNotification { Type = "General", Title = "Level 3: Puppy", Body = "My level is 3." };
        var mismatched = new UserNotification { Type = "LevelUp:4", Title = "Level 3: Puppy", Body = "Dosegel si level 3 in naslov Puppy." };

        Assert.Equal(custom.Title, NotificationPresentation.Title(custom));
        Assert.Equal(custom.Body, NotificationPresentation.Body(custom));
        Assert.Equal(mismatched.Title, NotificationPresentation.Title(mismatched));
        Assert.Equal(mismatched.Body, NotificationPresentation.Body(mismatched));
    }

    [Fact]
    public void CurrentAndPreviousYearUseLjubljanaCalendar()
    {
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        var currentYear = NotificationPresentation.Time(new DateTime(2026, 5, 19, 10, 0, 0, DateTimeKind.Utc), now);
        var previousYear = NotificationPresentation.Time(new DateTime(2025, 5, 19, 10, 0, 0, DateTimeKind.Utc), now);

        Assert.DoesNotContain("2026", currentYear);
        Assert.Contains("12:00", currentYear);
        Assert.Contains("2025", previousYear);
        Assert.Contains("12:00", previousYear);
    }

    [Fact]
    public void TodayAndYesterdayFollowLjubljanaDateAtYearBoundary()
    {
        var now = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal("danes · 00:15", NotificationPresentation.Time(new DateTime(2025, 12, 31, 23, 15, 0, DateTimeKind.Utc), now));
        Assert.Equal("včeraj · 23:15", NotificationPresentation.Time(new DateTime(2025, 12, 31, 22, 15, 0, DateTimeKind.Utc), now));
    }

    [Theory]
    [InlineData(2026, 1, 15, "13:00")]
    [InlineData(2026, 7, 15, "14:00")]
    public void LjubljanaWinterAndSummerOffsetsAreApplied(int year, int month, int day, string expectedTime)
    {
        var now = new DateTime(year, month, day, 18, 0, 0, DateTimeKind.Utc);
        var created = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal($"danes · {expectedTime}", NotificationPresentation.Time(created, now));
    }
}
