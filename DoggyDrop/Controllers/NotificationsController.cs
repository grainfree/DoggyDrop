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
        public async Task<IActionResult> MarkRead(int id, bool returnToInbox = false)
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

            var link = NotificationPresentation.Link(notification);
            if (!returnToInbox && !string.IsNullOrWhiteSpace(link) && Url.IsLocalUrl(link))
            {
                return Redirect(link);
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

            // Legacy visits remain history, not current facility recommendations.
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

            if (!hasDogs)
            {
                cards.Add(new SmartNotificationCard
                {
                    Type = "FirstDogProfile",
                    Title = "Dodaj prvega psa",
                    Body = "Pasji profil odklene osebne sprehode, statistiko in boljse predloge okoli tebe.",
                    LinkUrl = Url.Action("Create", "Dogs", new { firstDog = true, returnUrl = "/Map" })
                });
            }
            else if (!walkedToday)
            {
                cards.Add(new SmartNotificationCard
                {
                    Type = "WalkReminder",
                    Title = "Cas za sprehod",
                    Body = "Planner poti je pripravljen za hiter start iz tvoje trenutne lokacije.",
                    LinkUrl = Url.Action("Planner", "Walks")
                });
            }

            cards.Add(new SmartNotificationCard
            {
                Type = "NearbyMapTips",
                Title = "Blizu tebe",
                Body = "Predloge poti, najblizji kos in seznam lokacij odpres iz spodnjih gumbov na mapi.",
                LinkUrl = Url.Action("Index", "Map")
            });

            cards.Add(new SmartNotificationCard
            {
                Type = "WeatherAlert",
                Title = "Vremenski check",
                Body = "Telefon lahko pred sprehodom preveri dez, veter in visoke temperature.",
                LinkUrl = null
            });

            return cards;
        }

    }
}
