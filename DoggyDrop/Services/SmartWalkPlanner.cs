using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

// SmartWalkV1: all policy numbers live here; see docs/smart-walk-planner.md.
public static class SmartWalkPolicy
{
    public const string Version = "smart-v1";
    public const int CandidateLimit = 3, QueryLimitPerKind = 120, MaxGeometryPoints = 20000;
    public const double MetresPerMinute = 75, DurationTolerance = .20, MaximumDeviation = .50;
    public const double WaterCorridor = 25, BinCorridor = 25, ParkCorridor = 40;
    public const double DurationWeight = 100, DistanceWeight = 15, RepeatWeight = 25, DetourWeight = 30;
    public const double WaterBonus = 12, BinBonus = 6, ParkBonus = 16;
    public const double AnchorNetworkFactor = .75, MaximumAnchorDetour = 1.35;
    public const double EarthRadius = 6371000, EndpointSnapMetres = 75, MinimumGeometryMetres = 100;
    public const int MaximumConcurrentGenerations = 8, GenerationsPerMinute = 3;
    public static bool DurationValid(int minutes) => minutes is 15 or 30 or 45 or 60;
}
public sealed record SmartWalkInput(WalkingCoordinate Start, int Minutes, string WalkType,
    bool Water, bool Bins, bool Park, int Variant = 0, int? PlaceId = null)
{
    public bool IsValid => Start is { IsValid: true } && SmartWalkPolicy.DurationValid(Minutes)
        && Variant is >= 0 and <= 31 && (WalkType == "loop" && PlaceId == null || WalkType == "destination" && PlaceId > 0);
}
public sealed record SmartWalkPoi(int Id, string Kind, string Name, double Latitude, double Longitude)
{
    public WalkingCoordinate Coordinate => new(Latitude, Longitude);
}
public sealed record SmartWalkCandidate(int Index, IReadOnlyList<WalkingCoordinate> Anchors, IReadOnlyList<SmartWalkPoi> Stops);
public sealed record SmartWalkScore(double Total, double DurationDeviation, double DistanceDeviation,
    double Repetition, double DetourPenalty, double InfrastructureBonus);
public sealed record SmartWalkDiagnostic(int Candidate, string Status, SmartWalkScore? Score = null);
public sealed record SmartWalkSelection(SmartWalkInput Input, WalkingRouteResult Route, IReadOnlyList<SmartWalkPoi> Facts,
    IReadOnlyList<SmartWalkPoi> Stops, IReadOnlyList<SmartWalkDiagnostic> Diagnostics, int Attempts);

public sealed class SmartWalkPlanner(ApplicationDbContext db, IWalkingRoutes routes)
{
    public async Task<IReadOnlyList<SmartWalkPoi>> LoadNearbyAsync(SmartWalkInput input, CancellationToken ct)
    {
        var radius = input.Minutes * SmartWalkPolicy.MetresPerMinute / 2;
        var delta = radius / 110000;
        var lat = input.Start.Latitude; var lon = input.Start.Longitude;
        var south = Math.Max(-90, lat - delta); var north = Math.Min(90, lat + delta);
        var dx = Math.Min(180, delta / Math.Max(.000001, Math.Cos(Math.Max(Math.Abs(south), Math.Abs(north)) * Math.PI / 180)));
        var west = lon - dx; var east = lon + dx;
        // Three bounded projections, not an entity load or a query per vertex/POI.
        var bins = db.TrashBins.AsNoTracking().PublicBins().Select(p => new { p.Id, Kind = "bin", p.Name, p.Latitude, p.Longitude });
        var water = db.WaterPoints.AsNoTracking().PublicWater().Where(p => p.DogAccess != WaterDogAccess.NotAllowed)
            .Select(p => new { p.Id, Kind = "water", Name = p.Name == null || p.Name.Trim() == "" ? "Pitnik" : p.Name, p.Latitude, p.Longitude });
        var parks = db.Places.AsNoTracking().ForPublicDetails().Where(p => p.Category == PlaceCategory.DogPark)
            .Select(p => new { p.Id, Kind = "park", p.Name, p.Latitude, p.Longitude });
        var result = new List<SmartWalkPoi>();
        foreach (var query in new[] { bins, water, parks })
        {
            var rows = await query.Where(p => p.Latitude >= south && p.Latitude <= north &&
                (dx >= 180 || (west < -180 ? p.Longitude >= west + 360 || p.Longitude <= east :
                 east > 180 ? p.Longitude >= west || p.Longitude <= east - 360 : p.Longitude >= west && p.Longitude <= east)))
                .OrderBy(p => (p.Latitude - lat) * (p.Latitude - lat) + (p.Longitude - lon) * (p.Longitude - lon)).ThenBy(p => p.Id)
                .Take(SmartWalkPolicy.QueryLimitPerKind).ToListAsync(ct);
            result.AddRange(rows.Select(p => new SmartWalkPoi(p.Id, p.Kind, p.Name, p.Latitude, p.Longitude)).Where(p => p.Coordinate.IsValid
                && (p.Kind == "park" ? PublicPlaceEligibility.ValidName(p.Name) : !string.IsNullOrWhiteSpace(p.Name) && !p.Name.Any(char.IsControl))
                && SmartWalkGeometry.Distance(input.Start, p.Coordinate) <= radius));
        }
        return result;
    }
    public async Task<SmartWalkSelection?> GenerateAsync(SmartWalkInput input, CancellationToken ct)
    {
        if (!input.IsValid) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(26));
        var token = timeout.Token;
        var nearby = await LoadNearbyAsync(input, token);
        SmartWalkPoi? destination = null;
        if (input.PlaceId.HasValue)
        {
            destination = await db.Places.AsNoTracking().ForPublicDetails().Where(p => p.Id == input.PlaceId)
                .Select(p => new SmartWalkPoi(p.Id, "place", p.Name, p.Latitude, p.Longitude)).SingleOrDefaultAsync(token);
            if (destination == null || !PublicPlaceEligibility.ValidName(destination.Name)
                || SmartWalkGeometry.Distance(input.Start, destination.Coordinate) > input.Minutes * SmartWalkPolicy.MetresPerMinute * 1.2) return null;
        }
        var diagnostics = new List<SmartWalkDiagnostic>();
        SmartWalkSelection? best = null; double bestScore = double.NegativeInfinity; var attempts = 0;
        foreach (var candidate in SmartWalkGeometry.Candidates(input, nearby, destination).Take(SmartWalkPolicy.CandidateLimit))
        {
            ct.ThrowIfCancellationRequested();
            if (token.IsCancellationRequested) break;
            attempts++;
            WalkingRouteResult route;
            try { route = await routes.RouteAsync(candidate.Anchors, token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }
            if (!SmartWalkGeometry.ValidRoute(route, input, candidate))
            {
                diagnostics.Add(new(candidate.Index, route.Failure ?? "invalid-or-duration-outside-window"));
                if (route.Failure == "busy") break;
                continue;
            }
            var facts = SmartWalkGeometry.Facts(route.Points, nearby);
            var score = SmartWalkGeometry.Score(input, route, facts);
            diagnostics.Add(new(candidate.Index, "valid", score));
            if (score.Total > bestScore)
            {
                bestScore = score.Total;
                best = new(input, route, facts, candidate.Stops, [], attempts);
            }
        }
        return best == null ? null : best with { Diagnostics = diagnostics, Attempts = attempts };
    }
}

public static class SmartWalkGeometry
{
    public static double Distance(WalkingCoordinate a, WalkingCoordinate b) => DuplicateCandidates.Distance(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
    public static WalkingCoordinate Offset(WalkingCoordinate start, double metres, double heading)
    {
        var angular = metres / SmartWalkPolicy.EarthRadius; var bearing = heading * Math.PI / 180;
        var lat = start.Latitude * Math.PI / 180; var lon = start.Longitude * Math.PI / 180;
        var endLat = Math.Asin(Math.Sin(lat) * Math.Cos(angular) + Math.Cos(lat) * Math.Sin(angular) * Math.Cos(bearing));
        var endLon = lon + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat), Math.Cos(angular) - Math.Sin(lat) * Math.Sin(endLat));
        return new(endLat * 180 / Math.PI, ((endLon * 180 / Math.PI + 540) % 360) - 180);
    }
    public static IReadOnlyList<SmartWalkCandidate> Candidates(SmartWalkInput input, IReadOnlyList<SmartWalkPoi> nearby, SmartWalkPoi? destination = null)
    {
        if (input.WalkType == "destination") return destination == null ? [] : [new(0, [input.Start, destination.Coordinate], [destination])];
        var target = input.Minutes * SmartWalkPolicy.MetresPerMinute;
        var radius = target / (2 + Math.Sqrt(3)) * SmartWalkPolicy.AnchorNetworkFactor;
        var preferred = nearby.Where(p => p.Kind == "park" && input.Park || p.Kind == "water" && input.Water || p.Kind == "bin" && input.Bins)
            .OrderBy(p => p.Kind == "park" ? 0 : p.Kind == "water" ? 1 : 2).ThenBy(p => Distance(input.Start, p.Coordinate)).ThenBy(p => p.Id).ToArray();
        var candidates = new List<SmartWalkCandidate>();
        for (var i = 0; i < SmartWalkPolicy.CandidateLimit; i++)
        {
            var heading = input.Variant * 137.507764 + i * 120;
            var a = Offset(input.Start, radius, heading); var b = Offset(input.Start, radius, heading + 120);
            SmartWalkPoi? chosen = null;
            if (i > 0) chosen = preferred.Where(p => Distance(input.Start, p.Coordinate) + Distance(p.Coordinate, b) + Distance(b, input.Start)
                <= target * SmartWalkPolicy.AnchorNetworkFactor * SmartWalkPolicy.MaximumAnchorDetour).Skip(i - 1).FirstOrDefault();
            candidates.Add(new(i, [input.Start, chosen?.Coordinate ?? a, b, input.Start], chosen == null ? [] : [chosen]));
        }
        return candidates;
    }
    public static bool ValidRoute(WalkingRouteResult r, SmartWalkInput input, SmartWalkCandidate candidate)
    {
        if (!(r.IsRouted && r.Points.Count is >= 2 and <= SmartWalkPolicy.MaxGeometryPoints && r.Points.All(p => p is { IsValid: true })
        && double.IsFinite(r.DistanceMeters) && r.DistanceMeters > 0 && r.DurationSeconds is double seconds && double.IsFinite(seconds)
        && Math.Abs(seconds / (input.Minutes * 60d) - 1) <= SmartWalkPolicy.MaximumDeviation
        && Distance(r.Points[0], input.Start) <= SmartWalkPolicy.EndpointSnapMetres && Distance(r.Points[^1], candidate.Anchors[^1]) <= SmartWalkPolicy.EndpointSnapMetres)) return false;
        if (input.WalkType == "loop" && Distance(r.Points[0], r.Points[^1]) > SmartWalkPolicy.EndpointSnapMetres) return false;
        var length = 0d;
        for (var i = 1; i < r.Points.Count; i++) length += Distance(r.Points[i - 1], r.Points[i]);
        // Reject degenerate/malformed provider geometry rather than drawing an
        // approximate replacement. Summary remains provider-authoritative.
        if (length < SmartWalkPolicy.MinimumGeometryMetres || length > input.Minutes * SmartWalkPolicy.MetresPerMinute * 3) return false;
        foreach (var stop in candidate.Stops)
        {
            var corridor = stop.Kind == "place" ? SmartWalkPolicy.EndpointSnapMetres : stop.Kind == "park" ? SmartWalkPolicy.ParkCorridor : SmartWalkPolicy.WaterCorridor;
            if (!Enumerable.Range(1, r.Points.Count - 1).Any(i => SegmentDistance(stop.Coordinate, r.Points[i - 1], r.Points[i]) <= corridor)) return false;
        }
        return true;
    }
    // Local tangent-plane segment distance, antimeridian-aware, suitable for a <=60min walk.
    public static double SegmentDistance(WalkingCoordinate point, WalkingCoordinate a, WalkingCoordinate b)
    {
        static double Delta(double value) => (value + 540) % 360 - 180;
        var scale = Math.PI / 180 * SmartWalkPolicy.EarthRadius; var cos = Math.Cos(point.Latitude * Math.PI / 180);
        var ax = Delta(a.Longitude - point.Longitude) * scale * cos; var ay = (a.Latitude - point.Latitude) * scale;
        var bx = Delta(b.Longitude - point.Longitude) * scale * cos; var by = (b.Latitude - point.Latitude) * scale;
        var dx = bx - ax; var dy = by - ay; var length = dx * dx + dy * dy;
        var t = length == 0 ? 0 : Math.Clamp(-(ax * dx + ay * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(ax + t * dx, 2) + Math.Pow(ay + t * dy, 2));
    }
    public static IReadOnlyList<SmartWalkPoi> Facts(IReadOnlyList<WalkingCoordinate> route, IReadOnlyList<SmartWalkPoi> nearby)
    {
        // Both dimensions have hard limits: <=360 POIs and <=20,000 segments;
        // a latitude-band prefilter skips distant segments without changing the corridor.
        var result = new List<SmartWalkPoi>();
        foreach (var point in nearby.Take(SmartWalkPolicy.QueryLimitPerKind * 3))
        {
            var threshold = point.Kind == "park" ? SmartWalkPolicy.ParkCorridor : point.Kind == "water" ? SmartWalkPolicy.WaterCorridor : SmartWalkPolicy.BinCorridor;
            for (var i = 1; i < Math.Min(route.Count, SmartWalkPolicy.MaxGeometryPoints); i++)
            {
                if (point.Latitude < Math.Min(route[i - 1].Latitude, route[i].Latitude) - threshold / 110000
                    || point.Latitude > Math.Max(route[i - 1].Latitude, route[i].Latitude) + threshold / 110000) continue;
                if (SegmentDistance(point.Coordinate, route[i - 1], route[i]) <= threshold + 1e-7) { result.Add(point); break; }
            }
        }
        return result;
    }
    public static double Repetition(IReadOnlyList<WalkingCoordinate> points)
    {
        // Distance-weighted unoriented 20m geographic cells, resampled at <=10m.
        var seen = new HashSet<(int, int, int, int)>(); double repeated = 0, total = 0; var work = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var length = Distance(points[i - 1], points[i]); var steps = Math.Max(1, (int)Math.Ceiling(length / 10));
            for (var j = 1; j <= steps && work++ < 50000; j++)
            {
                (int, int) Cell(double t)
                {
                    var lat = points[i - 1].Latitude + (points[i].Latitude - points[i - 1].Latitude) * t;
                    static double Delta(double value) => (value + 540) % 360 - 180;
                    var lon = Delta(points[i - 1].Longitude - points[0].Longitude) + Delta(points[i].Longitude - points[i - 1].Longitude) * t;
                    return ((int)Math.Round(lat * 111195 / 20), (int)Math.Round(lon * 111195 * Math.Cos(points[0].Latitude * Math.PI / 180) / 20));
                }
                var a = Cell((j - 1d) / steps); var b = Cell(j / (double)steps); if (a == b) continue;
                if (a.CompareTo(b) > 0) (a, b) = (b, a); var key = (a.Item1, a.Item2, b.Item1, b.Item2);
                var metres = length / steps; total += metres; if (!seen.Add(key)) repeated += metres;
            }
        }
        return total == 0 ? 1 : repeated / total;
    }
    public static SmartWalkScore Score(SmartWalkInput input, WalkingRouteResult route, IReadOnlyList<SmartWalkPoi> facts)
    {
        var duration = Math.Abs(route.DurationSeconds!.Value / (input.Minutes * 60d) - 1);
        var distance = Math.Abs(route.DistanceMeters / (input.Minutes * SmartWalkPolicy.MetresPerMinute) - 1);
        var repetition = input.WalkType == "loop" ? Repetition(route.Points) : 0;
        var detour = Math.Max(0, route.DurationSeconds.Value / (input.Minutes * 60d) - 1 - SmartWalkPolicy.DurationTolerance) * SmartWalkPolicy.DetourWeight;
        var bonus = (input.Water && facts.Any(p => p.Kind == "water") ? SmartWalkPolicy.WaterBonus : 0)
            + (input.Bins ? Math.Min(2, facts.Count(p => p.Kind == "bin")) / 2d * SmartWalkPolicy.BinBonus : 0)
            + (input.Park && facts.Any(p => p.Kind == "park") ? SmartWalkPolicy.ParkBonus : 0);
        return new(100 - duration * SmartWalkPolicy.DurationWeight - distance * SmartWalkPolicy.DistanceWeight - repetition * SmartWalkPolicy.RepeatWeight - detour + bonus,
            duration, distance, repetition, detour, bonus);
    }
}
