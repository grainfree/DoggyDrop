using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public sealed record WalkingCoordinate(double Latitude, double Longitude)
{
    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Math.Abs(Latitude) <= 90 && Math.Abs(Longitude) <= 180;
}

public sealed record WalkingRouteResult(IReadOnlyList<WalkingCoordinate> Points,
    double DistanceMeters, double? DurationSeconds, string? Failure = null, int RetryAfterSeconds = 0)
{
    public bool IsRouted => Failure == null && Points.Count >= 2;
    public static WalkingRouteResult Unavailable(string reason, int retryAfter = 0) => new([], 0, null, reason, retryAfter);
}

public interface IWalkingRoutes
{
    Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points, CancellationToken cancellationToken = default);
}

// One bounded, process-wide budget shared by Home and Planner. No coordinates or identities retained.
public sealed class WalkingRouteBudget(TimeProvider clock)
{
    private readonly object gate = new();
    private DateTimeOffset minuteStart, dayStart, blockedUntil;
    private int minuteCount, dayCount;

    public bool TryTake(out int retryAfter)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (now >= minuteStart.AddMinutes(1)) { minuteStart = now; minuteCount = 0; }
            if (now >= dayStart.AddDays(1)) { dayStart = now; dayCount = 0; }
            var until = blockedUntil;
            if (minuteCount >= 30 && minuteStart.AddMinutes(1) > until) until = minuteStart.AddMinutes(1);
            if (dayCount >= 1800 && dayStart.AddDays(1) > until) until = dayStart.AddDays(1);
            retryAfter = Math.Max(0, (int)Math.Ceiling((until - now).TotalSeconds));
            if (retryAfter > 0) return false;
            minuteCount++; dayCount++; return true;
        }
    }

    public void BackOff(int seconds)
    {
        lock (gate)
        {
            var until = clock.GetUtcNow().AddSeconds(Math.Clamp(seconds, 60, 86400));
            if (until > blockedUntil) blockedUntil = until;
        }
    }
}

public sealed class OrsWalkingRoutes(HttpClient client, IConfiguration configuration,
    WalkingRouteBudget budget, ILogger<OrsWalkingRoutes> logger) : IWalkingRoutes
{
    public const string Endpoint = "https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson";
    public async Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points, CancellationToken cancellationToken = default)
    {
        if (points.Count is < 2 or > 12 || points.Any(p => p == null || !p.IsValid)
            || points.Distinct().Count() < 2 || DirectDistance(points) > 100_000)
            return WalkingRouteResult.Unavailable("invalid");
        var key = new[] { "OpenRouteService:ApiKey", "OpenRouteService__ApiKey", "OPENROUTESERVICE_API_KEY", "ORS_API_KEY" }
            .Select(name => configuration[name]).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (key == null) return WalkingRouteResult.Unavailable("unavailable");
        if (!budget.TryTake(out var retryAfter)) return WalkingRouteResult.Unavailable("busy", retryAfter);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = JsonContent.Create(new {
                    coordinates = points.Select(p => new[] { Math.Round(p.Longitude, 6), Math.Round(p.Latitude, 6) }),
                    preference = "recommended", instructions = false, elevation = false
                })
            };
            if (!request.Headers.TryAddWithoutValidation("Authorization", key))
                return WalkingRouteResult.Unavailable("unavailable");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = (int)Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60, 60, 86400);
                budget.BackOff(wait);
                return WalkingRouteResult.Unavailable("busy", wait);
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Walking routing unavailable: HTTP {Status}.", (int)response.StatusCode);
                return WalkingRouteResult.Unavailable("unavailable");
            }
            // Bound the response before parsing, including chunked responses.
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token)) != 0)
            {
                if (buffer.Length + count > 2_000_000) return WalkingRouteResult.Unavailable("unavailable");
                buffer.Write(chunk, 0, count);
            }
            using var json = JsonDocument.Parse(buffer.ToArray());
            var feature = json.RootElement.GetProperty("features")[0];
            var geometry = feature.GetProperty("geometry");
            if (geometry.GetProperty("type").GetString() != "LineString") return WalkingRouteResult.Unavailable("unavailable");
            var route = geometry.GetProperty("coordinates").EnumerateArray()
                .Select(p => new WalkingCoordinate(p[1].GetDouble(), p[0].GetDouble())).ToArray();
            var summary = feature.GetProperty("properties").GetProperty("summary");
            var distance = summary.GetProperty("distance").GetDouble();
            double? duration = summary.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds)
                && double.IsFinite(seconds) && seconds is > 0 and <= 604800 ? seconds : null;
            if (route.Length is < 2 or > 50000 || route.Any(p => !p.IsValid) || !double.IsFinite(distance) || distance <= 0)
                return WalkingRouteResult.Unavailable("unavailable");
            return new(route, distance, duration);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException
            or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException or FormatException or ArgumentException)
        {
            // Do not log exception bodies, coordinates, provider response bodies or credentials.
            logger.LogInformation("Walking routing unavailable ({Kind}).", ex.GetType().Name);
            return WalkingRouteResult.Unavailable("unavailable");
        }
    }

    private static double DirectDistance(IReadOnlyList<WalkingCoordinate> points)
    {
        double distance = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1]; var b = points[i]; const double rad = Math.PI / 180;
            var h = Math.Pow(Math.Sin((b.Latitude - a.Latitude) * rad / 2), 2)
                + Math.Cos(a.Latitude * rad) * Math.Cos(b.Latitude * rad) * Math.Pow(Math.Sin((b.Longitude - a.Longitude) * rad / 2), 2);
            distance += 6371000 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
        }
        return distance;
    }
}

public static class WalkingPlanRouting
{
    public const string ApproximateNotice = "Približen načrt: črte povezujejo postanke in niso preverjena pešpot. Preveri prehodnost poti. Čas hoje ni na voljo.";
    public static async Task<PlannedWalkRoute> ApplyAsync(PlannedWalkRoute plan, IWalkingRoutes? routes, CancellationToken cancellationToken)
    {
        var result = routes == null ? WalkingRouteResult.Unavailable("unavailable")
            : await routes.RouteAsync(plan.Stops.OrderBy(s => s.Order)
                .Select(s => new WalkingCoordinate(s.Latitude, s.Longitude)).ToArray(), cancellationToken);
        plan.IsWalkingRoute = result.IsRouted;
        plan.EstimatedMinutes = result.DurationSeconds.HasValue ? Math.Max(1, (int)Math.Ceiling(result.DurationSeconds.Value / 60)) : 0;
        if (result.IsRouted)
        {
            plan.RoutePoints = result.Points.Select(p => new PlannedWalkPoint { Latitude = p.Latitude, Longitude = p.Longitude }).ToArray();
            plan.EstimatedDistanceKm = result.DistanceMeters / 1000;
            plan.Summary += " Pešpot je izračunana z OpenRouteService; upoštevaj označbe in razmere na terenu.";
        }
        else plan.Summary += " " + ApproximateNotice;
        return plan;
    }
}
