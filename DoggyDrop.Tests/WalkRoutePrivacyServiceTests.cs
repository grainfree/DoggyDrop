using DoggyDrop.Models;
using DoggyDrop.Services;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkRoutePrivacyServiceTests
{
    private static readonly RouteCoordinate West1 = new(0, -0.004);
    private static readonly RouteCoordinate West2 = new(0, -0.003);
    private static readonly RouteCoordinate East1 = new(0, 0.003);
    private static readonly RouteCoordinate East2 = new(0, 0.004);
    private static readonly RouteCoordinate Center = new(0, 0);

    [Fact]
    public void NoZone_PreservesValidRoute()
    {
        var points = new[] { West1, West2, Center, East1, East2 };
        var result = WalkRoutePrivacyService.SanitizeForSharing(points, null, false);
        Assert.Equal(points, Assert.Single(result.Segments));
        Assert.False(result.HasHiddenGeometry);
    }

    [Fact]
    public void Owner_ReceivesFullRouteDespiteZone()
    {
        var points = new[] { West1, West2, Center, East1, East2 };
        var result = WalkRoutePrivacyService.SanitizeForSharing(points, Zone(), true);
        Assert.Equal(points, Assert.Single(result.Segments));
        Assert.False(result.HasHiddenGeometry);
    }

    [Fact]
    public void StartInside_RemovesActualStartAndUsesFirstVisiblePoint()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([Center, new(0, 0.0005), East1, East2], Zone(), false);
        Assert.Equal(new[] { East1, East2 }, Assert.Single(result.Segments));
        Assert.Equal(East1, result.Segments[0][0]);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void FinishInside_RemovesActualFinishAndUsesLastVisiblePoint()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([West1, West2, Center], Zone(), false);
        Assert.Equal(new[] { West1, West2 }, Assert.Single(result.Segments));
        Assert.Equal(West2, result.Segments[^1][^1]);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void StartAndFinishInside_RemovesBoth()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([Center, East1, East2, Center], Zone(), false);
        Assert.Equal(new[] { East1, East2 }, Assert.Single(result.Segments));
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void MiddleCrossing_CreatesSeparateSegmentsWithoutBridge()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([West1, West2, Center, East1, East2], Zone(), false);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(new[] { West1, West2 }, result.Segments[0]);
        Assert.Equal(new[] { East1, East2 }, result.Segments[1]);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void MultipleEntries_CreateMultipleSegments()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing(
            [West1, West2, Center, East1, East2, Center, West1, West2], Zone(), false);
        Assert.Equal(3, result.Segments.Count);
        Assert.All(result.Segments, segment => Assert.Equal(2, segment.Count));
    }

    [Fact]
    public void AllInside_ProducesNoPublicRoute()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([Center, new(0, 0.0005)], Zone(), false);
        Assert.Empty(result.Segments);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void AllOutside_PreservesRoute()
    {
        var points = new[] { East1, East2 };
        var result = WalkRoutePrivacyService.SanitizeForSharing(points, Zone(), false);
        Assert.Equal(points, Assert.Single(result.Segments));
        Assert.False(result.HasHiddenGeometry);
    }

    [Fact]
    public void OutsideEndpointsCrossingZone_NeverFormConnectingPolyline()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([West1, West2, East1, East2], Zone(), false);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(new[] { West1, West2 }, result.Segments[0]);
        Assert.Equal(new[] { East1, East2 }, result.Segments[1]);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void TangentAtBoundary_IsSplitConservatively()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing(
            [new(-0.003, -0.00179864), new(0.003, -0.00179864)], Zone(), false);
        Assert.Empty(result.Segments);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void NearMissOutsideRadius_RemainsConnected()
    {
        var points = new[] { new RouteCoordinate(-0.003, -0.0021), new RouteCoordinate(0.003, -0.0021) };
        var result = WalkRoutePrivacyService.SanitizeForSharing(points, Zone(), false);
        Assert.Equal(points, Assert.Single(result.Segments));
        Assert.False(result.HasHiddenGeometry);
    }

    [Fact]
    public void MultipleUnsampledCrossings_NeverBridgeZone()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing(
            [West1, West2, East1, East2, West1, West2], Zone(), false);
        Assert.Equal(3, result.Segments.Count);
        Assert.All(result.Segments, segment => Assert.Equal(2, segment.Count));
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void HighLatitude_UsesScaledLongitude()
    {
        var zone = Zone();
        zone.Latitude = 70;
        var west = new RouteCoordinate(70, -0.008);
        var east = new RouteCoordinate(70, 0.008);
        var result = WalkRoutePrivacyService.SanitizeForSharing([west, east], zone, false);
        Assert.Empty(result.Segments);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void RadiusBoundary_IsHiddenInsideAndVisibleOutside()
    {
        var inside = new RouteCoordinate(0, 0.00179); // Just under 200 m at the equator.
        var outside = new RouteCoordinate(0, 0.00181); // Just over 200 m.
        var result = WalkRoutePrivacyService.SanitizeForSharing([inside, outside, East1], Zone(), false);
        Assert.Equal(new[] { outside, East1 }, Assert.Single(result.Segments));
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void InvalidCoordinates_BreakSegmentsAndNeverReachOutput()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing(
            [West1, West2, new(double.NaN, 0), new(91, 0), East1, East2], Zone(), false);
        Assert.Equal(2, result.Segments.Count);
        Assert.DoesNotContain(result.Segments.SelectMany(segment => segment), point => !double.IsFinite(point.Latitude) || point.Latitude > 90);
    }

    [Fact]
    public void OneVisiblePoint_IsNotRenderedAsPolyline()
    {
        var result = WalkRoutePrivacyService.SanitizeForSharing([West1, Center, East1, East2], Zone(), false);
        Assert.Equal(new[] { East1, East2 }, Assert.Single(result.Segments));
    }

    [Fact]
    public void InvalidConfiguredZone_FailsClosed()
    {
        var zone = Zone();
        zone.Latitude = double.NaN;
        var result = WalkRoutePrivacyService.SanitizeForSharing([West1, West2], zone, false);
        Assert.Empty(result.Segments);
        Assert.True(result.HasHiddenGeometry);
    }

    [Fact]
    public void Sanitization_DoesNotMutateStoredPointsOrDistance()
    {
        var walk = new Walk
        {
            DistanceMeters = 5_000,
            Points = [new WalkPoint { Latitude = West1.Latitude, Longitude = West1.Longitude },
                new WalkPoint { Latitude = Center.Latitude, Longitude = Center.Longitude },
                new WalkPoint { Latitude = East1.Latitude, Longitude = East1.Longitude }]
        };
        WalkRoutePrivacyService.SanitizeForSharing(walk.Points.Select(point => new RouteCoordinate(point.Latitude, point.Longitude)), Zone(), false);
        Assert.Equal(5_000, walk.DistanceMeters);
        Assert.Equal(3, walk.Points.Count);
        Assert.Equal(Center.Latitude, walk.Points.ElementAt(1).Latitude);
    }

    private static PrivacyZone Zone() => new() { UserId = "owner", Latitude = 0, Longitude = 0, RadiusMeters = 200 };
}
