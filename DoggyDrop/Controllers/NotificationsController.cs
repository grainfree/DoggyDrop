using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public NotificationsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            await EnsureSmartNotificationsAsync(userId);

            var notifications = await _context.UserNotifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(80)
                .ToListAsync();

            return View(new NotificationsViewModel
            {
                Notifications = notifications,
                UnreadCount = notifications.Count(n => !n.IsRead),
                SmartCards = await BuildSmartCardsAsync(userId)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = _userManager.GetUserId(User);
            var notification = await _context.UserNotifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (notification == null)
            {
                return NotFound();
            }

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(notification.LinkUrl))
            {
                return Redirect(notification.LinkUrl);
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = _userManager.GetUserId(User);
            var unread = await _context.UserNotifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task EnsureSmartNotificationsAsync(string userId)
        {
            var today = DateTime.UtcNow.Date;
            var hasDogs = await _context.Dogs.AnyAsync(dog => dog.OwnerId == userId);
            var walkedToday = await _context.Walks.AnyAsync(walk =>
                walk.OwnerId == userId &&
                walk.Status == "Completed" &&
                walk.StartedAt >= today);

            if (hasDogs && !walkedToday)
            {
                await _notificationService.CreateUniqueRecentAsync(
                    userId,
                    "WalkReminder",
                    "Cas za sprehod",
                    "Danes se nisi zabelezil sprehoda. Hiter plan poti te ze caka.",
                    Url.Action("Planner", "Walks"),
                    withinHours: 18);
            }

            var recentVisit = await _context.DogParkVisits
                .Where(visit => visit.UserId == userId)
                .OrderByDescending(visit => visit.VisitedAt)
                .FirstOrDefaultAsync();

            if (recentVisit == null)
            {
                return;
            }

            var userVisitedKeys = await _context.DogParkVisits
                .Where(visit => visit.UserId == userId)
                .Select(visit => visit.PlaceKey)
                .Distinct()
                .ToListAsync();

            var parkCandidates = await _context.DogParkVisits
                .Where(visit => visit.PlaceKey != recentVisit.PlaceKey)
                .GroupBy(visit => new
                {
                    visit.PlaceKey,
                    visit.ParkName,
                    visit.Area,
                    visit.Address,
                    visit.Latitude,
                    visit.Longitude
                })
                .Select(group => new
                {
                    group.Key.PlaceKey,
                    group.Key.ParkName,
                    group.Key.Area,
                    group.Key.Address,
                    group.Key.Latitude,
                    group.Key.Longitude,
                    VisitCount = group.Count()
                })
                .ToListAsync();

            var popularNearbyPark = parkCandidates
                .Where(candidate => !userVisitedKeys.Contains(candidate.PlaceKey))
                .Select(candidate => new
                {
                    candidate.ParkName,
                    candidate.Area,
                    candidate.VisitCount,
                    DistanceKm = GetDistanceMeters(
                        recentVisit.Latitude,
                        recentVisit.Longitude,
                        candidate.Latitude,
                        candidate.Longitude) / 1000
                })
                .Where(candidate => candidate.DistanceKm <= 8)
                .OrderBy(candidate => candidate.DistanceKm)
                .ThenByDescending(candidate => candidate.VisitCount)
                .FirstOrDefault();

            if (popularNearbyPark != null)
            {
                var areaLabel = string.IsNullOrWhiteSpace(popularNearbyPark.Area)
                    ? "v tvoji okolici"
                    : $"na območju {popularNearbyPark.Area}";
                await _notificationService.CreateUniqueRecentAsync(
                    userId,
                    "PopularParkNearby",
                    "Popularen park v blizini",
                    $"{popularNearbyPark.ParkName} je med bolj obiskanimi pasjimi parki {areaLabel}.",
                    Url.Action("Index", "Map"),
                    withinHours: 24);
            }
        }

        private async Task<IReadOnlyList<SmartNotificationCard>> BuildSmartCardsAsync(string userId)
        {
            var cards = new List<SmartNotificationCard>();
            var today = DateTime.UtcNow.Date;

            var hasDogs = await _context.Dogs.AnyAsync(dog => dog.OwnerId == userId);
            var walkedToday = await _context.Walks.AnyAsync(walk =>
                walk.OwnerId == userId &&
                walk.Status == "Completed" &&
                walk.StartedAt >= today);

            if (hasDogs && !walkedToday)
            {
                cards.Add(new SmartNotificationCard
                {
                    Type = "WalkReminder",
                    Title = "Cas za sprehod",
                    Body = "Planner poti je pripravljen za hiter start iz tvoje trenutne lokacije.",
                    LinkUrl = Url.Action("Planner", "Walks")
                });
            }

            var recentVisit = await _context.DogParkVisits
                .Where(visit => visit.UserId == userId)
                .OrderByDescending(visit => visit.VisitedAt)
                .FirstOrDefaultAsync();

            if (recentVisit != null)
            {
                cards.Add(new SmartNotificationCard
                {
                    Type = "PopularParkNearby",
                    Title = "Predlog parka",
                    Body = $"Nazadnje si bil pri {recentVisit.ParkName}. Na mapi preveri se druge popularne parke v blizini.",
                    LinkUrl = Url.Action("Index", "Map")
                });
            }

            cards.Add(new SmartNotificationCard
            {
                Type = "WeatherAlert",
                Title = "Vremenski check",
                Body = "Telefon lahko pred sprehodom preveri dez, veter in visoke temperature.",
                LinkUrl = null
            });

            return cards;
        }

        private static double GetDistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadiusMeters = 6371000;
            var dLat = ToRadians(lat2 - lat1);
            var dLng = ToRadians(lng2 - lng1);
            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRadians(double value)
        {
            return value * Math.PI / 180;
        }
    }
}
