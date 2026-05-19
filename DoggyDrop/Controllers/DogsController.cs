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
    public class DogsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICloudinaryService _cloudinaryService;

        public DogsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ICloudinaryService cloudinaryService)
        {
            _context = context;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var dogs = await _context.Dogs
                .Where(d => d.OwnerId == userId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            return View(dogs);
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? returnUrl = null, bool firstDog = false)
        {
            var userId = _userManager.GetUserId(User);
            var ownedDogCount = string.IsNullOrWhiteSpace(userId)
                ? 0
                : await _context.Dogs.CountAsync(dog => dog.OwnerId == userId);

            return View(new DogCreateViewModel
            {
                ReturnUrl = returnUrl,
                IsFirstDog = firstDog || ownedDogCount == 0
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            var dog = await _context.Dogs
                .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId);

            if (dog == null)
            {
                return NotFound();
            }

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var completedWalks = await _context.Walks
                .Where(w => w.DogId == dog.Id && w.OwnerId == userId && w.Status == "Completed")
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();
            var parkVisits = await _context.DogParkVisits
                .Where(visit => visit.DogId == dog.Id && visit.UserId == userId)
                .OrderByDescending(visit => visit.VisitedAt)
                .ToListAsync();
            var totalDistanceKm = completedWalks.Sum(w => w.DistanceMeters) / 1000;

            var model = new DogDetailsViewModel
            {
                Dog = dog,
                RecentWalks = completedWalks.Take(5).ToList(),
                TotalDistanceKm = totalDistanceKm,
                CompletedWalkCount = completedWalks.Count,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                TotalDuration = TimeSpan.FromTicks(completedWalks
                    .Where(w => w.EndedAt.HasValue)
                    .Sum(w => (w.EndedAt!.Value - w.StartedAt).Ticks)),
                EstimatedCalories = EstimateCalories(totalDistanceKm, dog.Size),
                Achievements = GetDogAchievements(
                    completedWalks.Count,
                    completedWalks.Count(w => w.StartedAt >= weekStart),
                    totalDistanceKm,
                    completedWalks.Sum(w => w.UsedBinsCount),
                    parkVisits.Select(visit => visit.PlaceKey).Distinct().Count()),
                ActivityInsights = ActivityInsightsBuilder.Build(completedWalks, weeklyGoalKm: 7, monthlyGoalKm: 30),
                FavoriteParks = parkVisits
                    .GroupBy(visit => new
                    {
                        visit.PlaceKey,
                        visit.ParkName,
                        visit.Area
                    })
                    .Select(group => new FavoriteParkItem
                    {
                        ParkName = group.Key.ParkName,
                        Area = group.Key.Area,
                        VisitCount = group.Count(),
                        LastVisitedAt = group.Max(visit => visit.VisitedAt)
                    })
                    .OrderByDescending(item => item.VisitCount)
                    .ThenByDescending(item => item.LastVisitedAt)
                    .Take(5)
                    .ToList(),
                ParkVisitCount = parkVisits.Count
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var userId = _userManager.GetUserId(User);
            var dog = await _context.Dogs
                .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId);

            if (dog == null)
            {
                return NotFound();
            }

            return View(new DogEditViewModel
            {
                Id = dog.Id,
                Name = dog.Name,
                Breed = dog.Breed,
                AgeYears = dog.AgeYears,
                Gender = dog.Gender,
                Size = dog.Size,
                Character = dog.Character,
                NearbyVisibility = dog.NearbyVisibility,
                ApproximateLocation = GetApproximateLocationKey(dog.LastKnownLatitude, dog.LastKnownLongitude),
                CurrentPhotoUrl = dog.PhotoUrl
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DogCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            string? photoUrl = null;
            if (model.Photo != null && model.Photo.Length > 0)
            {
                photoUrl = await _cloudinaryService.UploadImageAsync(model.Photo);
                if (string.IsNullOrWhiteSpace(photoUrl))
                {
                    ModelState.AddModelError(nameof(model.Photo), "Fotografije ni bilo mogoce shraniti. Poskusi z JPG, PNG, WEBP ali HEIC sliko.");
                    return View(model);
                }
            }

            var dog = new Dog
            {
                Name = model.Name,
                Breed = model.Breed,
                AgeYears = model.AgeYears,
                Gender = model.Gender,
                Size = model.Size,
                Character = model.Character,
                PhotoUrl = photoUrl,
                OwnerId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Dogs.Add(dog);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = model.IsFirstDog
                ? $"{dog.Name} je zdaj del DoggyDrop. Cas je za prvi sprehod."
                : $"{dog.Name} je dodan v DoggyDrop.";
            TempData["ShowFirstWalkPrompt"] = model.IsFirstDog;
            TempData["FirstDogName"] = dog.Name;

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return model.IsFirstDog
                ? RedirectToAction("Index", "Map")
                : RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DogEditViewModel model)
        {
            if (id != model.Id)
            {
                return BadRequest();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var userId = _userManager.GetUserId(User);
            var dog = await _context.Dogs
                .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId);

            if (dog == null)
            {
                return NotFound();
            }

            dog.Name = model.Name;
            dog.Breed = model.Breed;
            dog.AgeYears = model.AgeYears;
            dog.Gender = model.Gender;
            dog.Size = model.Size;
            dog.Character = model.Character;
            dog.NearbyVisibility = NormalizeVisibility(model.NearbyVisibility);
            ApplyApproximateLocation(dog, model.ApproximateLocation);

            if (model.Photo != null && model.Photo.Length > 0)
            {
                var photoUrl = await _cloudinaryService.UploadImageAsync(model.Photo);
                if (string.IsNullOrWhiteSpace(photoUrl))
                {
                    ModelState.AddModelError(nameof(model.Photo), "Fotografije ni bilo mogoce shraniti.");
                    model.CurrentPhotoUrl = dog.PhotoUrl;
                    return View(model);
                }

                dog.PhotoUrl = photoUrl;
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{dog.Name} je posodobljen.";
            return RedirectToAction(nameof(Details), new { id = dog.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePhoto(int id, IFormFile photo)
        {
            var userId = _userManager.GetUserId(User);
            var dog = await _context.Dogs
                .FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId);

            if (dog == null)
            {
                return NotFound();
            }

            if (photo == null || photo.Length == 0)
            {
                TempData["ErrorMessage"] = "Izberi fotografijo psa.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var photoUrl = await _cloudinaryService.UploadImageAsync(photo);
            if (string.IsNullOrWhiteSpace(photoUrl))
            {
                TempData["ErrorMessage"] = "Fotografije ni bilo mogoce shraniti.";
                return RedirectToAction(nameof(Details), new { id });
            }

            dog.PhotoUrl = photoUrl;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Fotografija psa je posodobljena.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private static IReadOnlyList<AchievementItem> GetDogAchievements(
            int completedWalks,
            int walksThisWeek,
            double totalDistanceKm,
            int usedBinsCount,
            int uniqueParkCount)
        {
            return
            [
                BuildAchievement("Explorer", "Zakljuci prvi sprehod.", completedWalks, 1, suffix: "sprehodov"),
                BuildAchievement("City walker", "Prehodi 10 km.", totalDistanceKm, 10, suffix: "km"),
                BuildAchievement("Trail master", "Prehodi 100 km.", totalDistanceKm, 100, suffix: "km"),
                BuildAchievement("Weekly streak", "Zakljuci 3 sprehode ta teden.", walksThisWeek, 3, suffix: "ta teden"),
                BuildAchievement("Bin buddy", "Uporabi 5 pasjih kosev med sprehodi.", usedBinsCount, 5, suffix: "uporab"),
                BuildAchievement("Park explorer", "Obisci 5 razlicnih pasjih parkov.", uniqueParkCount, 5, suffix: "parkov")
            ];
        }

        private static AchievementItem BuildAchievement(string name, string description, double current, double target, string suffix)
        {
            var safeTarget = Math.Max(target, 1);
            var progressPercent = (int)Math.Min(100, Math.Round(current / safeTarget * 100));
            var currentText = suffix == "km" ? current.ToString("0.0") : Math.Floor(current).ToString("0");
            var targetText = suffix == "km" ? target.ToString("0") : target.ToString("0");

            return new AchievementItem
            {
                Name = name,
                Description = description,
                IsUnlocked = current >= target,
                ProgressPercent = progressPercent,
                ProgressText = $"{currentText} / {targetText} {suffix}"
            };
        }

        private static int EstimateCalories(double distanceKm, string? dogSize)
        {
            var caloriesPerKm = dogSize?.Trim().ToLowerInvariant() switch
            {
                "majhen" or "small" => 45,
                "velik" or "large" => 85,
                _ => 65
            };

            return (int)Math.Round(distanceKm * caloriesPerKm);
        }

        private static string NormalizeVisibility(string? visibility)
        {
            return visibility is "Visible" or "FriendsOnly" ? visibility : "Invisible";
        }

        private static void ApplyApproximateLocation(Dog dog, string? locationKey)
        {
            var location = GetLocationCoordinates(locationKey);
            if (location == null)
            {
                dog.LastKnownLatitude = null;
                dog.LastKnownLongitude = null;
                dog.LastLocationUpdatedAt = null;
                return;
            }

            dog.LastKnownLatitude = location.Value.Latitude;
            dog.LastKnownLongitude = location.Value.Longitude;
            dog.LastLocationUpdatedAt = DateTime.UtcNow;
        }

        private static string? GetApproximateLocationKey(double? latitude, double? longitude)
        {
            if (!latitude.HasValue || !longitude.HasValue)
            {
                return null;
            }

            return GetKnownLocations()
                .OrderBy(location => Math.Pow(location.Value.Latitude - latitude.Value, 2) + Math.Pow(location.Value.Longitude - longitude.Value, 2))
                .FirstOrDefault().Key;
        }

        private static (double Latitude, double Longitude)? GetLocationCoordinates(string? locationKey)
        {
            var locations = GetKnownLocations();
            return !string.IsNullOrWhiteSpace(locationKey) && locations.TryGetValue(locationKey, out var location)
                ? location
                : null;
        }

        private static IReadOnlyDictionary<string, (double Latitude, double Longitude)> GetKnownLocations()
        {
            return new Dictionary<string, (double Latitude, double Longitude)>
            {
                ["maribor"] = (46.5547, 15.6459),
                ["ljubljana"] = (46.0569, 14.5058),
                ["celje"] = (46.2397, 15.2677),
                ["kranj"] = (46.2397, 14.3556),
                ["koper"] = (45.5481, 13.7301)
            };
        }
    }
}
