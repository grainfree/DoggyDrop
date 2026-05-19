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
    public class FriendsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public FriendsController(
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
            return View(await BuildModelAsync());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendRequest(string userId)
        {
            var currentUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(userId) || userId == currentUserId)
            {
                return BadRequest();
            }

            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                return NotFound();
            }

            var existing = await _context.Friendships.FirstOrDefaultAsync(f =>
                (f.RequesterId == currentUserId && f.AddresseeId == userId) ||
                (f.RequesterId == userId && f.AddresseeId == currentUserId));

            if (existing == null)
            {
                _context.Friendships.Add(new Friendship
                {
                    RequesterId = currentUserId,
                    AddresseeId = userId,
                    Status = "Pending",
                    CreatedAt = DateTime.UtcNow
                });
                await _notificationService.CreateAsync(
                    userId,
                    "FriendRequest",
                    "Nova prosnja za prijateljstvo",
                    "Nekdo iz DoggyDrop skupnosti se zeli povezati s tabo.",
                    Url.Action("Index", "Friends"));
                TempData["SuccessMessage"] = "Prosnja za prijateljstvo je poslana.";
            }
            else if (existing.Status == "Declined" || existing.Status == "Removed")
            {
                existing.RequesterId = currentUserId;
                existing.AddresseeId = userId;
                existing.Status = "Pending";
                existing.CreatedAt = DateTime.UtcNow;
                existing.RespondedAt = null;
                await _notificationService.CreateAsync(
                    userId,
                    "FriendRequest",
                    "Nova prosnja za prijateljstvo",
                    "Nekdo iz DoggyDrop skupnosti se zeli povezati s tabo.",
                    Url.Action("Index", "Friends"));
                TempData["SuccessMessage"] = "Prosnja za prijateljstvo je ponovno poslana.";
            }
            else
            {
                TempData["ErrorMessage"] = "Povezava s tem uporabnikom ze obstaja.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Accept(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == currentUserId && f.Status == "Pending");

            if (friendship == null)
            {
                return NotFound();
            }

            friendship.Status = "Accepted";
            friendship.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _notificationService.CreateAsync(
                friendship.RequesterId,
                "FriendAccepted",
                "Prosnja je sprejeta",
                "Tvoja prosnja za prijateljstvo je bila sprejeta.",
                Url.Action("Index", "Friends"));

            TempData["SuccessMessage"] = "Prosnja je sprejeta.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Decline(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            var friendship = await _context.Friendships
                .FirstOrDefaultAsync(f => f.Id == id && f.AddresseeId == currentUserId && f.Status == "Pending");

            if (friendship == null)
            {
                return NotFound();
            }

            friendship.Status = "Declined";
            friendship.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prosnja je zavrnjena.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            var friendship = await _context.Friendships.FirstOrDefaultAsync(f =>
                f.Id == id &&
                f.Status == "Accepted" &&
                (f.RequesterId == currentUserId || f.AddresseeId == currentUserId));

            if (friendship == null)
            {
                return NotFound();
            }

            friendship.Status = "Removed";
            friendship.RespondedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Prijatelj je odstranjen.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<FriendsViewModel> BuildModelAsync()
        {
            var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);

            var friendships = await _context.Friendships
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .Where(f => f.RequesterId == currentUserId || f.AddresseeId == currentUserId)
                .ToListAsync();

            var connectedUserIds = friendships
                .Where(f => f.Status is "Pending" or "Accepted")
                .Select(f => f.RequesterId == currentUserId ? f.AddresseeId : f.RequesterId)
                .ToHashSet();

            var accepted = friendships
                .Where(f => f.Status == "Accepted")
                .Select(f => new
                {
                    Friendship = f,
                    User = f.RequesterId == currentUserId ? f.Addressee : f.Requester
                })
                .Where(item => item.User != null)
                .ToList();

            var acceptedIds = accepted.Select(item => item.User!.Id).ToList();
            var dogCounts = await _context.Dogs
                .Where(d => acceptedIds.Contains(d.OwnerId))
                .GroupBy(d => d.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count);

            var weeklyDistances = await _context.Walks
                .Where(w => acceptedIds.Contains(w.OwnerId) && w.Status == "Completed" && w.StartedAt >= weekStart)
                .GroupBy(w => w.OwnerId)
                .Select(g => new { OwnerId = g.Key, DistanceKm = g.Sum(w => w.DistanceMeters) / 1000 })
                .ToDictionaryAsync(x => x.OwnerId, x => x.DistanceKm);

            var incoming = friendships
                .Where(f => f.Status == "Pending" && f.AddresseeId == currentUserId && f.Requester != null)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FriendRequestItem
                {
                    FriendshipId = f.Id,
                    UserId = f.RequesterId,
                    Name = GetDisplayName(f.Requester),
                    PhotoUrl = f.Requester?.ProfileImageUrl,
                    CreatedAt = f.CreatedAt
                })
                .ToList();

            var outgoing = friendships
                .Where(f => f.Status == "Pending" && f.RequesterId == currentUserId && f.Addressee != null)
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FriendRequestItem
                {
                    FriendshipId = f.Id,
                    UserId = f.AddresseeId,
                    Name = GetDisplayName(f.Addressee),
                    PhotoUrl = f.Addressee?.ProfileImageUrl,
                    CreatedAt = f.CreatedAt
                })
                .ToList();

            var suggestions = await BuildSuggestionsAsync(currentUserId, connectedUserIds, weekStart);

            return new FriendsViewModel
            {
                Friends = accepted
                    .OrderBy(item => GetDisplayName(item.User))
                    .Select(item => new FriendItem
                    {
                        FriendshipId = item.Friendship.Id,
                        UserId = item.User!.Id,
                        Name = GetDisplayName(item.User),
                        PhotoUrl = item.User.ProfileImageUrl,
                        DogCount = dogCounts.GetValueOrDefault(item.User.Id),
                        WeeklyDistanceKm = weeklyDistances.GetValueOrDefault(item.User.Id),
                        FriendsSince = item.Friendship.RespondedAt ?? item.Friendship.CreatedAt
                    })
                    .ToList(),
                IncomingRequests = incoming,
                OutgoingRequests = outgoing,
                Suggestions = suggestions,
                FriendCount = accepted.Count,
                IncomingCount = incoming.Count
            };
        }

        private async Task<IReadOnlyList<FriendSuggestionItem>> BuildSuggestionsAsync(string currentUserId, HashSet<string> connectedUserIds, DateTime weekStart)
        {
            var candidates = await _context.Users
                .Where(u => u.Id != currentUserId && !connectedUserIds.Contains(u.Id))
                .OrderBy(u => u.DisplayName ?? u.Email)
                .Take(30)
                .ToListAsync();

            var candidateIds = candidates.Select(u => u.Id).ToList();
            var dogCounts = await _context.Dogs
                .Where(d => candidateIds.Contains(d.OwnerId))
                .GroupBy(d => d.OwnerId)
                .Select(g => new { OwnerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.OwnerId, x => x.Count);

            var weeklyDistances = await _context.Walks
                .Where(w => candidateIds.Contains(w.OwnerId) && w.Status == "Completed" && w.StartedAt >= weekStart)
                .GroupBy(w => w.OwnerId)
                .Select(g => new { OwnerId = g.Key, DistanceKm = g.Sum(w => w.DistanceMeters) / 1000 })
                .ToDictionaryAsync(x => x.OwnerId, x => x.DistanceKm);

            return candidates
                .Where(user => dogCounts.ContainsKey(user.Id) || weeklyDistances.ContainsKey(user.Id))
                .OrderByDescending(user => weeklyDistances.GetValueOrDefault(user.Id))
                .ThenByDescending(user => dogCounts.GetValueOrDefault(user.Id))
                .Take(8)
                .Select(user => new FriendSuggestionItem
                {
                    UserId = user.Id,
                    Name = GetDisplayName(user),
                    PhotoUrl = user.ProfileImageUrl,
                    DogCount = dogCounts.GetValueOrDefault(user.Id),
                    WeeklyDistanceKm = weeklyDistances.GetValueOrDefault(user.Id)
                })
                .ToList();
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? "DoggyDrop uporabnik";
        }
    }
}
