using DoggyDrop.Services;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Route("api/leaderboards")]
    public class LeaderboardsApiController : ControllerBase
    {
        private readonly ILocalLeaderboardService _localLeaderboardService;

        public LeaderboardsApiController(ILocalLeaderboardService localLeaderboardService)
        {
            _localLeaderboardService = localLeaderboardService;
        }

        [HttpGet("local")]
        public async Task<IActionResult> Local(string? city = null)
        {
            var board = await _localLeaderboardService.BuildAsync(city);
            return Ok(new
            {
                Cities = _localLeaderboardService.Cities.Select(item => new { item.Key, item.Name }),
                Board = board
            });
        }
    }
}
