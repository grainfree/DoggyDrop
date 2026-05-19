using DoggyDrop.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/trashbins")]
    public class TrashBinsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TrashBinsApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("nearby")]
        public async Task<IActionResult> Nearby(double? latitude, double? longitude, double radiusKm = 5)
        {
            var bins = await _context.TrashBins
                .Include(bin => bin.User)
                .Where(bin => bin.IsApproved)
                .ToListAsync();

            var binItems = bins
                .Select(bin => new
                {
                    bin.Id,
                    bin.Name,
                    bin.Latitude,
                    bin.Longitude,
                    ImageUrl = bin.FullImageUrl,
                    AddedBy = bin.User != null && !string.IsNullOrWhiteSpace(bin.User.DisplayName)
                        ? bin.User.DisplayName
                        : "DoggyDrop uporabnik",
                    bin.UsedCount,
                    bin.FullReports,
                    bin.MissingReports,
                    bin.UsefulVotes
                })
                .ToList();

            if (!latitude.HasValue || !longitude.HasValue)
            {
                return Ok(binItems.Take(100));
            }

            var safeRadiusKm = Math.Clamp(radiusKm, 0.5, 50);
            var nearby = binItems
                .Select(bin => new
                {
                    bin.Id,
                    bin.Name,
                    bin.Latitude,
                    bin.Longitude,
                    bin.ImageUrl,
                    bin.AddedBy,
                    bin.UsedCount,
                    bin.FullReports,
                    bin.MissingReports,
                    bin.UsefulVotes,
                    DistanceKm = GetDistanceKm(latitude.Value, longitude.Value, bin.Latitude, bin.Longitude)
                })
                .Where(bin => bin.DistanceKm <= safeRadiusKm)
                .OrderBy(bin => bin.DistanceKm)
                .Take(100)
                .ToList();

            return Ok(nearby);
        }

        private static double GetDistanceKm(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadiusKm = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLng = ToRadians(lng2 - lng1);
            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRadians(double value)
        {
            return value * Math.PI / 180;
        }
    }
}
