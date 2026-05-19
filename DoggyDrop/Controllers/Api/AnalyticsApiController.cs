using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/analytics")]
    public class AnalyticsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary()
        {
            var bins = await _context.TrashBins.ToListAsync();
            var completedWalks = await _context.Walks
                .Where(walk => walk.Status == "Completed")
                .ToListAsync();
            var openPlaydates = await _context.PlaydateRequests.CountAsync(playdate =>
                playdate.Status == "Open" && playdate.PreferredAt >= DateTime.UtcNow.AddHours(-2));

            return Ok(new
            {
                Bins = new
                {
                    Total = bins.Count,
                    Approved = bins.Count(bin => bin.IsApproved),
                    Pending = bins.Count(bin => !bin.IsApproved),
                    Uses = bins.Sum(bin => bin.UsedCount),
                    FullReports = bins.Sum(bin => bin.FullReports),
                    MissingReports = bins.Sum(bin => bin.MissingReports)
                },
                Walks = new
                {
                    Completed = completedWalks.Count,
                    TotalKm = completedWalks.Sum(walk => walk.DistanceMeters) / 1000,
                    UsedBins = completedWalks.Sum(walk => walk.UsedBinsCount)
                },
                Community = new
                {
                    Dogs = await _context.Dogs.CountAsync(),
                    OpenPlaydates = openPlaydates
                },
                TopBins = bins
                    .OrderByDescending(bin => bin.UsedCount)
                    .Take(5)
                    .Select(bin => new
                    {
                        bin.Id,
                        bin.Name,
                        bin.UsedCount,
                        bin.FullReports,
                        bin.MissingReports
                    })
            });
        }
    }
}
