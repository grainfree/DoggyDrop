using DoggyDrop.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/community-map")]
    public class CommunityMapApiController : ControllerBase
    {
        private const double PresenceLatitudeCellDegrees = 0.00225;
        private const double PresenceLongitudeCellDegrees = 0.00325;
        private static readonly TimeSpan PresenceFreshness = TimeSpan.FromMinutes(8);
        private readonly ApplicationDbContext _context;

        public CommunityMapApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("heatmap")]
        public async Task<IActionResult> Heatmap(string range = "today")
        {
            var now = DateTime.UtcNow;
            var normalizedRange = NormalizeRange(range);
            var from = normalizedRange switch
            {
                "week" => now.AddDays(-7),
                "evening" => now.Date.AddDays(-7),
                _ => now.Date
            };

            var activePoints = _context.WalkPoints.AsNoTracking()
                .Where(point => point.RecordedAt >= now - PresenceFreshness && point.RecordedAt <= now &&
                    point.Walk != null && point.Walk.Status == "Active" &&
                    !_context.PrivacyZones.Any(zone => zone.UserId == point.Walk.OwnerId));

            var activeLocations = await activePoints
                .Select(point => new
                {
                    point.Latitude,
                    point.Longitude,
                    point.RecordedAt,
                    point.WalkId,
                    OwnerId = point.Walk!.OwnerId
                })
                .ToListAsync();

            var activeWalkers = activeLocations
                .Where(point => double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude) &&
                    point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180)
                .GroupBy(point => point.OwnerId)
                .Select(group => group.OrderByDescending(point => point.RecordedAt).ThenByDescending(point => point.WalkId).First())
                .ToList();

            // A fixed ~250 m cell in Slovenia keeps repeated positions stable within the cell.
            // A single walker never creates a public location, even at this coarse precision.
            var activeHotspots = activeWalkers
                .GroupBy(point => (
                    LatitudeCell: Math.Floor(point.Latitude / PresenceLatitudeCellDegrees),
                    LongitudeCell: Math.Floor(point.Longitude / PresenceLongitudeCellDegrees)))
                .Where(group => group.Count() >= 2)
                .Select(group => new CommunityHotspot(
                    "active",
                    (group.Key.LatitudeCell + 0.5) * PresenceLatitudeCellDegrees,
                    (group.Key.LongitudeCell + 0.5) * PresenceLongitudeCellDegrees,
                    group.Count(),
                    group.Count() == 2 ? "2 aktivna sprehajalca" : $"{group.Count()} aktivnih sprehajalcev",
                    "Približna aktivnost na območju",
                    Math.Min(1, group.Count() / 8d)))
                .OrderByDescending(item => item.Count)
                .Take(20)
                .ToList();

            var walkPointsQuery = _context.WalkPoints.AsNoTracking()
                .Where(point => point.RecordedAt >= from && point.Walk != null &&
                    point.Walk.Status == "Completed" &&
                    !_context.PrivacyZones.Any(zone => zone.UserId == point.Walk.OwnerId));

            if (normalizedRange == "evening")
            {
                walkPointsQuery = walkPointsQuery.Where(point => point.RecordedAt.Hour >= 17 && point.RecordedAt.Hour <= 22);
            }

            var walkPoints = await walkPointsQuery
                .Select(point => new
                {
                    point.Latitude,
                    point.Longitude,
                    point.WalkId,
                    OwnerId = point.Walk!.OwnerId
                })
                .ToListAsync();

            var routeHotspots = walkPoints
                .Where(point => double.IsFinite(point.Latitude) && double.IsFinite(point.Longitude) &&
                    point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180)
                .GroupBy(point => (
                    LatitudeCell: Math.Floor(point.Latitude / PresenceLatitudeCellDegrees),
                    LongitudeCell: Math.Floor(point.Longitude / PresenceLongitudeCellDegrees)))
                .Select(group => new
                {
                    group.Key,
                    Count = group.Select(point => point.WalkId).Distinct().Count(),
                    Contributors = group.Select(point => point.OwnerId).Distinct().Count()
                })
                .Where(cell => cell.Contributors >= 2)
                .Select(cell => new CommunityHotspot(
                    "route",
                    (cell.Key.LatitudeCell + 0.5) * PresenceLatitudeCellDegrees,
                    (cell.Key.LongitudeCell + 0.5) * PresenceLongitudeCellDegrees,
                    cell.Count,
                    "Priljubljena pot",
                    $"{cell.Count} sprehodov na tem območju",
                    Math.Min(1, cell.Count / 8d)))
                .OrderByDescending(item => item.Count)
                .Take(18)
                .ToList();

            var parkVisitsQuery = _context.DogParkVisits.Where(visit => visit.VisitedAt >= from);
            if (normalizedRange == "evening")
            {
                parkVisitsQuery = parkVisitsQuery.Where(visit => visit.VisitedAt.Hour >= 17 && visit.VisitedAt.Hour <= 22);
            }

            var parkVisits = await parkVisitsQuery.ToListAsync();
            var parkHotspots = parkVisits
                .GroupBy(visit => new
                {
                    visit.PlaceKey,
                    visit.ParkName,
                    visit.Latitude,
                    visit.Longitude
                })
                .Select(group => new CommunityHotspot(
                    "park",
                    group.Key.Latitude,
                    group.Key.Longitude,
                    group.Count(),
                    group.Key.ParkName,
                    group.Count() == 1
                        ? "1 pes je bil tukaj"
                        : $"{group.Count()} psov je bilo tukaj",
                    Math.Min(1, group.Count() / 12d)))
                .OrderByDescending(item => item.Count)
                .Take(12)
                .ToList();

            var visibleDogs = await _context.Dogs
                .Where(dog =>
                    dog.NearbyVisibility == "Visible" &&
                    dog.LastKnownLatitude.HasValue &&
                    dog.LastKnownLongitude.HasValue &&
                    dog.LastLocationUpdatedAt.HasValue &&
                    dog.LastLocationUpdatedAt >= now.AddDays(-7))
                .ToListAsync();

            var dogDensity = visibleDogs
                .GroupBy(dog => Bucket(dog.LastKnownLatitude!.Value, dog.LastKnownLongitude!.Value, 3))
                .Select(group => new CommunityHotspot(
                    "dog",
                    group.Average(dog => dog.LastKnownLatitude!.Value),
                    group.Average(dog => dog.LastKnownLongitude!.Value),
                    group.Count(),
                    "Dog density",
                    group.Count() == 1
                        ? "1 viden pes v blizini"
                        : $"{group.Count()} vidnih psov v blizini",
                    Math.Min(1, group.Count() / 8d)))
                .OrderByDescending(item => item.Count)
                .Take(12)
                .ToList();

            var allHotspots = activeHotspots
                .Concat(parkHotspots)
                .Concat(routeHotspots)
                .Concat(dogDensity)
                .OrderByDescending(item => item.Type == "active" ? 4 : item.Type == "park" ? 3 : item.Type == "route" ? 2 : 1)
                .ThenByDescending(item => item.Count)
                .ToList();

            return Ok(new
            {
                Range = normalizedRange,
                GeneratedAt = now,
                ActiveWalkers = activeWalkers.Count,
                PopularParks = parkHotspots.Count,
                TrendingRoutes = routeHotspots.Count,
                DogDensitySpots = dogDensity.Count,
                Hotspots = allHotspots,
                Events = allHotspots
                    .Where(item => item.Type is "park" or "route" or "active")
                    .Take(5)
                    .Select(item => new
                    {
                        item.Type,
                        item.Title,
                        item.Subtitle,
                        item.Count
                    })
            });
        }

        private static string NormalizeRange(string range)
        {
            return range?.Trim().ToLowerInvariant() switch
            {
                "week" => "week",
                "evening" => "evening",
                _ => "today"
            };
        }

        private static string Bucket(double latitude, double longitude, int decimals)
        {
            return $"{Math.Round(latitude, decimals):0.000}:{Math.Round(longitude, decimals):0.000}";
        }

        private sealed record CommunityHotspot(
            string Type,
            double Latitude,
            double Longitude,
            int Count,
            string Title,
            string Subtitle,
            double Intensity);
    }
}
