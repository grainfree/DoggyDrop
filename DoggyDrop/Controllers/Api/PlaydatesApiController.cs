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
    [Route("api/playdates")]
    public class PlaydatesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public PlaydatesApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        [HttpGet("open")]
        public async Task<IActionResult> Open()
        {
            var requests = await _context.PlaydateRequests
                .Include(p => p.Dog)
                .Include(p => p.Owner)
                .Include(p => p.Interests)
                .Where(p => p.Status == "Open" && p.PreferredAt >= DateTime.UtcNow.AddHours(-2))
                .OrderBy(p => p.PreferredAt)
                .Take(50)
                .Select(p => new
                {
                    p.Id,
                    DogId = p.DogId,
                    DogName = p.Dog != null ? p.Dog.Name : "Pes",
                    DogPhotoUrl = p.Dog != null ? p.Dog.PhotoUrl : null,
                    OwnerName = p.Owner != null && !string.IsNullOrWhiteSpace(p.Owner.DisplayName)
                        ? p.Owner.DisplayName
                        : "DoggyDrop uporabnik",
                    p.LocationLabel,
                    p.PreferredAt,
                    p.SizePreference,
                    p.EnergyLevel,
                    p.Note,
                    InterestedCount = p.Interests != null
                        ? p.Interests.Count(interest => interest.Status == "Interested")
                        : 0
                })
                .ToListAsync();

            return Ok(requests);
        }

        [Authorize]
        [HttpPost("quick-invite")]
        public async Task<IActionResult> QuickInvite([FromBody] QuickPlaydateInviteInput input)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var myDog = await _context.Dogs
                .FirstOrDefaultAsync(dog => dog.Id == input.SourceDogId && dog.OwnerId == userId);
            if (myDog == null)
            {
                return BadRequest(new { message = "Izbrani pes ni veljaven." });
            }

            var targetDog = await _context.Dogs
                .Include(dog => dog.Owner)
                .FirstOrDefaultAsync(dog => dog.Id == input.TargetDogId);
            if (targetDog == null || string.IsNullOrWhiteSpace(targetDog.OwnerId) || targetDog.OwnerId == userId)
            {
                return BadRequest(new { message = "Ciljni pes ni veljaven." });
            }

            var preferredAt = DateTime.UtcNow.AddHours(2);
            var request = new PlaydateRequest
            {
                DogId = myDog.Id,
                OwnerId = userId,
                LocationLabel = string.IsNullOrWhiteSpace(input.LocationLabel) ? "Blizu tebe" : input.LocationLabel.Trim(),
                PreferredAt = preferredAt,
                SizePreference = string.IsNullOrWhiteSpace(myDog.Size) ? "Vse velikosti" : myDog.Size,
                EnergyLevel = string.IsNullOrWhiteSpace(input.EnergyLevel) ? "Srednja" : input.EnergyLevel.Trim(),
                Note = $"Hitri pozdrav za {targetDog.Name}. {TrimToLength(input.Message, 140)}".Trim(),
                Status = "Open",
                CreatedAt = DateTime.UtcNow
            };

            _context.PlaydateRequests.Add(request);
            await _context.SaveChangesAsync();

            await _notificationService.CreateAsync(
                targetDog.OwnerId,
                "PlaydateInvite",
                "Povabilo na sprehod",
                $"{myDog.Name} zeli hiter sprehod blizu lokacije {request.LocationLabel}.",
                "/Playdates");

            return Ok(new
            {
                request.Id,
                request.LocationLabel,
                PreferredAt = request.PreferredAt
            });
        }

        private static string? TrimToLength(string? value, int maxLength)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    public class QuickPlaydateInviteInput
    {
        public int SourceDogId { get; set; }

        public int TargetDogId { get; set; }

        public string? LocationLabel { get; set; }

        public string? EnergyLevel { get; set; }

        public string? Message { get; set; }
    }
}
