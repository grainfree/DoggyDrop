using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/friends")]
    public class FriendsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public FriendsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Mine()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var friendships = await _context.Friendships
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .Where(f => f.Status == "Accepted" && (f.RequesterId == userId || f.AddresseeId == userId))
                .ToListAsync();

            return Ok(friendships.Select(f =>
            {
                var friend = f.RequesterId == userId ? f.Addressee : f.Requester;
                return new
                {
                    friendshipId = f.Id,
                    userId = friend?.Id,
                    name = !string.IsNullOrWhiteSpace(friend?.DisplayName)
                        ? friend.DisplayName
                        : friend?.Email ?? "DoggyDrop uporabnik",
                    photoUrl = friend?.ProfileImageUrl,
                    friendsSince = f.RespondedAt ?? f.CreatedAt
                };
            }));
        }

        [HttpGet("requests")]
        public async Task<IActionResult> Requests()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var incoming = await _context.Friendships
                .Include(f => f.Requester)
                .Where(f => f.Status == "Pending" && f.AddresseeId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new
                {
                    friendshipId = f.Id,
                    userId = f.RequesterId,
                    name = f.Requester != null && !string.IsNullOrWhiteSpace(f.Requester.DisplayName)
                        ? f.Requester.DisplayName
                        : f.Requester != null ? f.Requester.Email : "DoggyDrop uporabnik",
                    photoUrl = f.Requester != null ? f.Requester.ProfileImageUrl : null,
                    f.CreatedAt
                })
                .ToListAsync();

            return Ok(incoming);
        }
    }
}
