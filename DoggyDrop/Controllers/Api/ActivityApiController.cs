using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/activity")]
    public class ActivityApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IGamificationService _gamificationService;

        public ActivityApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IGamificationService gamificationService)
        {
            _context = context;
            _userManager = userManager;
            _gamificationService = gamificationService;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> Summary(int? dogId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (dogId.HasValue)
            {
                var dogExists = await _context.Dogs.AnyAsync(d => d.Id == dogId.Value && d.OwnerId == userId);
                if (!dogExists)
                {
                    return NotFound();
                }
            }

            var walks = await _context.Walks
                .Where(w => w.OwnerId == userId && w.Status == "Completed" && (!dogId.HasValue || w.DogId == dogId.Value))
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();

            var insights = ActivityInsightsBuilder.Build(
                walks,
                weeklyGoalKm: dogId.HasValue ? 7 : 10,
                monthlyGoalKm: dogId.HasValue ? 30 : 40);

            object? walkStreak = null;
            if (!dogId.HasValue)
            {
                var canonicalWalkStreak = await _gamificationService.GetStreakAsync(userId, GamificationStreakConstants.Walk);
                insights.CurrentStreakDays = canonicalWalkStreak.EffectiveCurrentDays;
                insights.LongestStreakDays = canonicalWalkStreak.LongestDays;
                walkStreak = new
                {
                    currentDays = canonicalWalkStreak.EffectiveCurrentDays,
                    longestDays = canonicalWalkStreak.LongestDays,
                    state = canonicalWalkStreak.State.ToString(),
                    canonicalWalkStreak.IsSafeToday,
                    canonicalWalkStreak.IsAtRiskToday,
                    canonicalWalkStreak.Guidance
                };
            }

            return Ok(new
            {
                totalWalks = walks.Count,
                totalDistanceKm = walks.Sum(w => w.DistanceMeters) / 1000,
                totalUsedBins = walks.Sum(w => w.UsedBinsCount),
                insights,
                walkStreak
            });
        }
    }
}
