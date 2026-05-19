using DoggyDrop.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/community-map")]
    public class CommunityMapApiController : ControllerBase
    {
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

            var walkPointsQuery = _context.WalkPoints
                .Include(point => point.Walk)
                .Where(point => point.RecordedAt >= from && point.Walk != null);

            if (normalizedRange == "evening")
            {
                walkPointsQuery = walkPointsQuery.Where(point => point.RecordedAt.Hour >= 17 && point.RecordedAt.Hour <= 22);
            }

            var walkPoints = await walkPointsQuery
                .Select(point => new
                {
                    point.Latitude,
                    point.Longitude,
                    point.RecordedAt,
                    WalkStatus = point.Walk!.Status,
                    point.WalkId
                })
                .ToListAsync();

            var activeWalks = walkPoints
                .Where(point => point.WalkStatus == "Active" && point.RecordedAt >= now.AddHours(-2))
                .GroupBy(point => point.WalkId)
                .Select(group => group.OrderByDescending(point => point.RecordedAt).First())
                .ToList();

            var routeHotspots = walkPoints
                .Where(point => point.WalkStatus == "Completed")
                .GroupBy(point => Bucket(point.Latitude, point.Longitude, 3))
                .Select(group => new CommunityHotspot(
                    "route",
                    group.Average(point => point.Latitude),
                    group.Average(point => point.Longitude),
                    group.Select(point => point.WalkId).Distinct().Count(),
                    "Trending route",
                    $"{group.Select(point => point.WalkId).Distinct().Count()} sprehodov na tem obmocju",
                    Math.Min(1, group.Select(point => point.WalkId).Distinct().Count() / 8d)))
                .Where(item => item.Count > 0)
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

            var activeHotspots = activeWalks
                .Select(point => new CommunityHotspot(
                    "active",
                    point.Latitude,
                    point.Longitude,
                    1,
                    "Active walker",
                    "Sprehod v teku",
                    1))
                .Take(20)
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
                ActiveWalkers = activeHotspots.Count,
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
