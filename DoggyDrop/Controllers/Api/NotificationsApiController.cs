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
    [Route("api/notifications")]
    public class NotificationsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
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

            var notifications = await _context.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(30)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.Body,
                    n.LinkUrl,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                unreadCount = notifications.Count(n => !n.IsRead),
                notifications
            });
        }

        [HttpGet("center")]
        public async Task<IActionResult> Center()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var latest = await _context.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(6)
                .Select(n => new
                {
                    n.Id,
                    n.Type,
                    n.Title,
                    n.Body,
                    n.LinkUrl,
                    n.IsRead,
                    n.CreatedAt
                })
                .ToListAsync();

            var unreadCount = await _context.UserNotifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            var latestUnread = latest.Where(n => !n.IsRead).ToList();

            return Ok(new
            {
                unreadCount,
                latest,
                latestUnread,
                smart = await BuildSmartCardsAsync(userId)
            });
        }

        [HttpPost("{id:int}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var notification = await _context.UserNotifications
                .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);

            if (notification == null)
            {
                return NotFound();
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                notification.Id,
                notification.LinkUrl,
                notification.IsRead
            });
        }

        [HttpGet("smart")]
        public async Task<IActionResult> Smart()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var items = await BuildSmartCardsAsync(userId);
            return Ok(new { items });
        }

        private async Task<IReadOnlyList<object>> BuildSmartCardsAsync(string userId)
        {
            var items = new List<object>();
            var today = DateTime.UtcNow.Date;

            var hasDogs = await _context.Dogs.AnyAsync(dog => dog.OwnerId == userId);
            var walkedToday = await _context.Walks.AnyAsync(walk =>
                walk.OwnerId == userId &&
                walk.Status == "Completed" &&
                walk.StartedAt >= today);

            if (!hasDogs)
            {
                items.Add(new
                {
                    Type = "FirstDogProfile",
                    Title = "Dodaj prvega psa",
                    Body = "Pasji profil odklene osebne sprehode, statistiko in boljse predloge okoli tebe.",
                    LinkUrl = "/Dogs/Create?firstDog=true&returnUrl=/Map"
                });
            }
            else if (!walkedToday)
            {
                items.Add(new
                {
                    Type = "WalkReminder",
                    Title = "Cas za sprehod",
                    Body = "Planner poti je pripravljen za hiter start iz tvoje trenutne lokacije.",
                    LinkUrl = "/Walks/Planner"
                });
            }

            var recentVisit = await _context.DogParkVisits
                .Where(visit => visit.UserId == userId)
                .OrderByDescending(visit => visit.VisitedAt)
                .FirstOrDefaultAsync();

            if (recentVisit != null)
            {
                items.Add(new
                {
                    Type = "PopularParkNearby",
                    Title = "Predlog parka",
                    Body = $"Nazadnje si bil pri {recentVisit.ParkName}. Na mapi preveri se druge popularne parke v blizini.",
                    LinkUrl = "/Map"
                });
            }

            items.Add(new
            {
                Type = "NearbyMapTips",
                Title = "Blizu tebe",
                Body = "Predloge poti, najblizji kos in seznam lokacij odpres iz spodnjih gumbov na mapi.",
                LinkUrl = "/Map"
            });

            items.Add(new
            {
                Type = "WeatherAlert",
                Title = "Vremenski check",
                Body = "Telefon lahko pred sprehodom preveri dez, veter in visoke temperature.",
                LinkUrl = string.Empty
            });

            return items;
        }
    }
}
