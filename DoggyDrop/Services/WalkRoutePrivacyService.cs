using DoggyDrop.Models;

namespace DoggyDrop.Services;

public readonly record struct RouteCoordinate(double Latitude, double Longitude);

public sealed record WalkRoutePrivacyResult(
    IReadOnlyList<IReadOnlyList<RouteCoordinate>> Segments,
    bool HasHiddenGeometry);

public static class WalkRoutePrivacyService
{
    private const double EarthRadiusMeters = 6_371_000;

    // Prepared for an explicit route-sharing feature. Walk Details still exposes no geometry to non-owners.
    public static WalkRoutePrivacyResult SanitizeForSharing(
        IEnumerable<RouteCoordinate> points, PrivacyZone? zone, bool isOwner)
    {
        var segments = new List<IReadOnlyList<RouteCoordinate>>();
        var current = new List<RouteCoordinate>();
        var hidden = false;
        var invalidZone = !isOwner && zone != null &&
            (!IsValid(zone.Latitude, zone.Longitude) || zone.RadiusMeters <= 0);
        if (invalidZone) return new WalkRoutePrivacyResult([], true);

        foreach (var point in points)
        {
            if (!IsValid(point.Latitude, point.Longitude))
            {
                hidden = true;
                Flush();
                continue;
            }

            if (!isOwner && zone != null && DistanceMeters(point, zone) <= zone.RadiusMeters)
            {
                hidden = true;
                Flush();
                continue;
            }

            if (!isOwner && zone != null && current.Count > 0 &&
                SegmentTouchesZone(current[^1], point, zone))
            {
                hidden = true;
                Flush();
            }

            current.Add(point);
        }

        Flush();
        return new WalkRoutePrivacyResult(segments, hidden);

        void Flush()
        {
            if (current.Count >= 2) segments.Add(current.ToArray());
            current.Clear();
        }
    }

    private static bool IsValid(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) &&
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static bool SegmentTouchesZone(RouteCoordinate start, RouteCoordinate end, PrivacyZone zone)
    {
        // For 200-1000 m zones, project around the zone center with longitude scaled by
        // cos(latitude). The 1 m guard treats tangencies and projection roundoff as private.
        var latitudeScale = Math.PI / 180 * EarthRadiusMeters;
        var longitudeScale = latitudeScale * Math.Cos(zone.Latitude * Math.PI / 180);
        var ax = WrappedLongitudeDelta(start.Longitude, zone.Longitude) * longitudeScale;
        var ay = (start.Latitude - zone.Latitude) * latitudeScale;
        var bx = WrappedLongitudeDelta(end.Longitude, zone.Longitude) * longitudeScale;
        var by = (end.Latitude - zone.Latitude) * latitudeScale;
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var fraction = lengthSquared == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / lengthSquared, 0, 1);
        var nearestX = ax + fraction * dx;
        var nearestY = ay + fraction * dy;
        var guardedRadius = zone.RadiusMeters + 1;
        return nearestX * nearestX + nearestY * nearestY <= guardedRadius * guardedRadius;
    }

    private static double WrappedLongitudeDelta(double longitude, double center) =>
        (longitude - center + 540) % 360 - 180;

    private static double DistanceMeters(RouteCoordinate point, PrivacyZone zone)
    {
        var latitudeDelta = (point.Latitude - zone.Latitude) * Math.PI / 180;
        var longitudeDelta = (point.Longitude - zone.Longitude) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
            Math.Cos(zone.Latitude * Math.PI / 180) * Math.Cos(point.Latitude * Math.PI / 180) *
            Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
