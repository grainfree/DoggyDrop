using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DoggyDrop.Controllers
{
    [Authorize]
    public class CityController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CityController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var model = await BuildDashboardModelAsync();
            return View(model);
        }

        internal async Task<CityDashboardViewModel> BuildDashboardModelAsync()
        {
            var bins = await _context.TrashBins
                .OrderByDescending(bin => bin.DateAdded)
                .ToListAsync();

            var completedWalks = await _context.Walks
                .Where(walk => walk.Status == "Completed")
                .ToListAsync();

            var activeDogs = await _context.Dogs.CountAsync();
            var openPlaydates = await _context.PlaydateRequests.CountAsync(playdate =>
                playdate.Status == "Open" && playdate.PreferredAt >= DateTime.UtcNow.AddHours(-2));

            return new CityDashboardViewModel
            {
                TotalBins = bins.Count,
                ApprovedBins = bins.Count(bin => bin.IsApproved),
                PendingBins = bins.Count(bin => !bin.IsApproved),
                FullReports = bins.Sum(bin => bin.FullReports),
                MissingReports = bins.Sum(bin => bin.MissingReports),
                BinUses = bins.Sum(bin => bin.UsedCount),
                CompletedWalks = completedWalks.Count,
                TotalWalkKm = completedWalks.Sum(walk => walk.DistanceMeters) / 1000,
                ActiveDogs = activeDogs,
                OpenPlaydates = openPlaydates,
                BinIssues = bins
                    .Where(bin => bin.FullReports > 0 || bin.MissingReports > 0)
                    .OrderByDescending(bin => bin.FullReports + bin.MissingReports)
                    .ThenByDescending(bin => bin.LastReportedAt)
                    .Take(8)
                    .Select(bin => new CityBinIssueItem
                    {
                        Id = bin.Id,
                        Name = bin.Name,
                        FullReports = bin.FullReports,
                        MissingReports = bin.MissingReports,
                        LastReportedAt = bin.LastReportedAt
                    })
                    .ToList(),
                TopBins = bins
                    .Where(bin => bin.UsedCount > 0 || bin.UsefulVotes > 0)
                    .OrderByDescending(bin => bin.UsedCount)
                    .ThenByDescending(bin => bin.UsefulVotes)
                    .Take(8)
                    .Select(bin => new CityTopBinItem
                    {
                        Id = bin.Id,
                        Name = bin.Name,
                        UsedCount = bin.UsedCount,
                        UsefulVotes = bin.UsefulVotes
                    })
                    .ToList(),
                WeeklyActivity = BuildWeeklyActivity(completedWalks)
            };
        }

        private static IReadOnlyList<CityWeeklyActivityItem> BuildWeeklyActivity(IReadOnlyList<Walk> walks)
        {
            var today = DateTime.UtcNow.Date;
            var days = Enumerable.Range(0, 7)
                .Select(offset => today.AddDays(offset - 6))
                .ToList();

            var daily = days
                .Select(day =>
                {
                    var dayWalks = walks.Where(walk => walk.StartedAt.Date == day).ToList();
                    return new CityWeeklyActivityItem
                    {
                        DayLabel = day.ToLocalTime().ToString("ddd", new CultureInfo("sl-SI")),
                        WalkCount = dayWalks.Count,
                        DistanceKm = dayWalks.Sum(walk => walk.DistanceMeters) / 1000
                    };
                })
                .ToList();

            var maxDistance = Math.Max(daily.Max(day => day.DistanceKm), 0.1);
            foreach (var day in daily)
            {
                day.IntensityPercent = (int)Math.Round(day.DistanceKm / maxDistance * 100);
            }

            return daily;
        }
    }
}
