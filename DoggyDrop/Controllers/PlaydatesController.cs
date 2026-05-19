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
    public class PlaydatesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public PlaydatesController(
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
            return View(await BuildModelAsync(new PlaydateCreateViewModel()));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PlaydateCreateViewModel model)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var dog = await _context.Dogs.FirstOrDefaultAsync(d => d.Id == model.DogId && d.OwnerId == userId);
            if (dog == null)
            {
                ModelState.AddModelError(nameof(model.DogId), "Izbrani pes ni najden.");
            }

            if (!ModelState.IsValid)
            {
                return View("Index", await BuildModelAsync(model));
            }

            var request = new PlaydateRequest
            {
                DogId = model.DogId,
                OwnerId = userId,
                LocationLabel = model.LocationLabel,
                PreferredAt = DateTime.SpecifyKind(model.PreferredAt, DateTimeKind.Local).ToUniversalTime(),
                SizePreference = model.SizePreference,
                EnergyLevel = model.EnergyLevel,
                Note = model.Note,
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            _context.PlaydateRequests.Add(request);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Playdate povabilo je objavljeno.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            var userId = _userManager.GetUserId(User);
            var request = await _context.PlaydateRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.OwnerId == userId);

            if (request == null)
            {
                return NotFound();
            }

            request.Status = "Closed";
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Playdate povabilo je zaprto.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Interested(int id, PlaydateInterestViewModel interest)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var request = await _context.PlaydateRequests
                .FirstOrDefaultAsync(p => p.Id == id && p.Status == "Open");

            if (request == null)
            {
                return NotFound();
            }

            if (request.OwnerId == userId)
            {
                TempData["ErrorMessage"] = "Na svoje povabilo se ne rabis prijaviti.";
                return RedirectToAction(nameof(Index));
            }

            var dog = await _context.Dogs.FirstOrDefaultAsync(d => d.Id == interest.DogId && d.OwnerId == userId);
            if (dog == null)
            {
                TempData["ErrorMessage"] = "Izbrani pes ni najden.";
                return RedirectToAction(nameof(Index));
            }

            var existing = await _context.PlaydateInterests
                .FirstOrDefaultAsync(i => i.PlaydateRequestId == id && i.OwnerId == userId);

            if (existing == null)
            {
                _context.PlaydateInterests.Add(new PlaydateInterest
                {
                    PlaydateRequestId = id,
                    DogId = dog.Id,
                    OwnerId = userId,
                    Message = interest.Message,
                    Status = "Interested",
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.DogId = dog.Id;
                existing.Message = interest.Message;
                existing.Status = "Interested";
            }

            await _context.SaveChangesAsync();
            await _notificationService.CreateAsync(
                request.OwnerId,
                "PlaydateInterest",
                "Nov odziv na playdate",
                $"{dog.Name} se zanima za tvoje playdate povabilo.",
                Url.Action("Index", "Playdates"));

            TempData["SuccessMessage"] = "Zanimanje za playdate je poslano.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawInterest(int id)
        {
            var userId = _userManager.GetUserId(User);
            var interest = await _context.PlaydateInterests
                .FirstOrDefaultAsync(i => i.PlaydateRequestId == id && i.OwnerId == userId);

            if (interest == null)
            {
                return NotFound();
            }

            interest.Status = "Withdrawn";
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Zanimanje je umaknjeno.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<PlaydatesViewModel> BuildModelAsync(PlaydateCreateViewModel create)
        {
            var userId = _userManager.GetUserId(User);
            var dogs = await _context.Dogs
                .Where(d => d.OwnerId == userId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            if (create.DogId == 0 && dogs.Count > 0)
            {
                create.DogId = dogs[0].Id;
            }

            var openRequests = await _context.PlaydateRequests
                .Include(p => p.Dog)
                .Include(p => p.Owner)
                .Include(p => p.Interests!.Where(i => i.Status == "Interested"))
                    .ThenInclude(i => i.Dog)
                .Include(p => p.Interests!.Where(i => i.Status == "Interested"))
                    .ThenInclude(i => i.Owner)
                .Where(p => p.Status == "Open" && p.PreferredAt >= DateTime.UtcNow.AddHours(-2))
                .OrderBy(p => p.PreferredAt)
                .Take(30)
                .ToListAsync();

            return new PlaydatesViewModel
            {
                MyDogs = dogs,
                OpenRequests = openRequests,
                Create = create,
                Interest = new PlaydateInterestViewModel
                {
                    DogId = dogs.FirstOrDefault()?.Id ?? 0
                }
            };
        }
    }
}
