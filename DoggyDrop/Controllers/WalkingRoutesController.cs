using System.Text.Json.Serialization;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoggyDrop.Controllers;

[AllowAnonymous]
public sealed class WalkingRoutesController(IWalkingRoutes routes) : Controller
{
    // Presence is required independently of numeric value: explicit zero remains valid.
    public sealed record CoordinateInput(
        [property: JsonRequired] double Latitude,
        [property: JsonRequired] double Longitude)
    {
        public WalkingCoordinate ToCoordinate() => new(Latitude, Longitude);
    }

    public sealed record RouteInput(CoordinateInput? Origin, CoordinateInput? Destination);

    [HttpPost("/api/walking-route")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("walking-route")]
    [RequestSizeLimit(4096)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Calculate([FromBody] RouteInput? input, CancellationToken cancellationToken)
    {
        var origin = input?.Origin?.ToCoordinate();
        var destination = input?.Destination?.ToCoordinate();
        if (!ModelState.IsValid || origin is not { IsValid: true } || destination is not { IsValid: true })
            return BadRequest(new { error = "invalid" });
        var result = await routes.RouteAsync([origin, destination], cancellationToken);
        if (!result.IsRouted)
        {
            if (result.RetryAfterSeconds > 0) Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(result.Failure == "busy" ? 429 : result.Failure == "invalid" ? 400 : 503,
                new { error = result.Failure });
        }
        return Json(new { points = result.Points.Select(p => new[] { p.Latitude, p.Longitude }),
            distanceMeters = result.DistanceMeters, durationSeconds = result.DurationSeconds });
    }
}
