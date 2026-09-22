using DoggyDrop.Models;
using DoggyDrop.Services;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkStalenessTests
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OldWalkWithRecentGpsIsNotStale()
    {
        var walk = new Walk { StartedAt = Now.AddDays(-3), Status = "Active" };
        var points = new[] { new WalkPoint { RecordedAt = Now.AddMinutes(-5) } };

        Assert.False(WalkStaleness.IsStale(walk, points, Now));
    }

    [Fact]
    public void OldWalkEndsAtLastGpsPointNotCurrentTime()
    {
        var walk = new Walk { StartedAt = Now.AddDays(-3), Status = "Active" };
        var lastPoint = Now.AddDays(-2);
        var points = new[] { new WalkPoint { RecordedAt = lastPoint } };

        Assert.True(WalkStaleness.IsStale(walk, points, Now));
        Assert.Equal(lastPoint, WalkStaleness.LastActivity(walk, points, Now));
    }

    [Fact]
    public void WalkWithoutGpsUsesStartAndExactlyOneDayIsNotStale()
    {
        var walk = new Walk { StartedAt = Now.Subtract(WalkStaleness.InactivityLimit), Status = "Active" };

        Assert.False(WalkStaleness.IsStale(walk, [], Now));
        Assert.True(WalkStaleness.IsStale(walk, [], Now.AddTicks(1)));
        Assert.Equal(walk.StartedAt, WalkStaleness.LastActivity(walk, [], Now));
    }

    [Fact]
    public void FutureAndPreStartPointsDoNotExtendActivity()
    {
        var walk = new Walk { StartedAt = Now.AddDays(-2), Status = "Active" };
        var points = new[]
        {
            new WalkPoint { RecordedAt = Now.AddDays(1) },
            new WalkPoint { RecordedAt = walk.StartedAt.AddMinutes(-1) }
        };

        Assert.True(WalkStaleness.IsStale(walk, points, Now));
        Assert.Equal(walk.StartedAt, WalkStaleness.LastActivity(walk, points, Now));
    }

    [Fact]
    public void CompletedWalkIsNeverRecovered()
    {
        var walk = new Walk { StartedAt = Now.AddDays(-5), Status = "Completed" };
        Assert.False(WalkStaleness.IsStale(walk, [], Now));
    }
}
