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
        private readonly IDogProgressionService _dogProgressionService;
        private readonly IUserAchievementService _userAchievementService;

        public DogsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ICloudinaryService cloudinaryService,
            IDogProgressionService dogProgressionService,
            IUserAchievementService userAchievementService)
        {
            _context = context;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
            _dogProgressionService = dogProgressionService;
            _userAchievementService = userAchievementService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? dogId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();
            var dogs = await _context.Dogs
                .AsNoTracking()
                .Where(d => d.OwnerId == userId)
                .OrderBy(d => d.Name)
                .ToListAsync();
            if (dogId.HasValue && dogs.All(dog => dog.Id != dogId.Value)) return NotFound();
            var selectedDog = dogId.HasValue ? dogs.First(dog => dog.Id == dogId.Value) : dogs.FirstOrDefault();
            var model = new DogsDashboardViewModel { Dogs = dogs, SelectedDog = selectedDog };
            if (selectedDog != null)
            {
                var activeWalk = await _context.Walks.AsNoTracking()
                    .Where(walk => walk.OwnerId == userId && walk.Status == "Active")
                    .OrderByDescending(walk => walk.StartedAt)
                    .ThenByDescending(walk => walk.Id)
                    .Select(walk => new { walk.Id, DogName = walk.Dog != null && walk.Dog.OwnerId == userId ? walk.Dog.Name : null })
                    .FirstOrDefaultAsync();
                model.ActiveWalkId = activeWalk?.Id;
                model.ActiveWalkDogName = activeWalk?.DogName;

                var completed = _context.Walks.AsNoTracking()
                    .Where(walk => walk.DogId == selectedDog.Id && walk.OwnerId == userId && walk.Status == "Completed");
                model.CompletedWalkCount = await completed.CountAsync();
                model.TotalDistanceKm = await completed.SumAsync(walk => walk.DistanceMeters) / 1000;
                model.RecentWalks = await completed.OrderByDescending(walk => walk.EndedAt ?? walk.StartedAt)
                    .ThenByDescending(walk => walk.Id)
                    .Take(3).ToListAsync();
                model.RecentPhotos = await _context.WalkPhotos.AsNoTracking()
                    .Where(photo => photo.UserId == userId && photo.Walk != null &&
                        photo.Walk.OwnerId == userId && photo.Walk.DogId == selectedDog.Id && photo.Walk.Status == "Completed" &&
                        photo.ImageUrl != "")
                    .OrderByDescending(photo => photo.CreatedAt).ThenByDescending(photo => photo.Id)
                    .Take(6).ToListAsync();
                model.ParkLocationCount = await _context.DogParkVisits.AsNoTracking()
                    .Where(visit => visit.DogId == selectedDog.Id && visit.UserId == userId)
                    .Select(visit => visit.PlaceKey).Distinct().CountAsync();
                var progression = await _context.DogProgressionProfiles.AsNoTracking()
                    .FirstOrDefaultAsync(profile => profile.DogId == selectedDog.Id);
                if (progression != null)
                {
                    model.Progression = progression;
                    model.Level = _dogProgressionService.CalculateLevelInfo(progression.TotalXp);
                }
            }

            return View(model);
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
                .AsNoTracking()
                .Where(w => w.DogId == dog.Id && w.OwnerId == userId && w.Status == "Completed")
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();
            var parkVisits = await _context.DogParkVisits
                .AsNoTracking()
                .Where(visit => visit.DogId == dog.Id && visit.UserId == userId)
                .OrderByDescending(visit => visit.VisitedAt)
                .ToListAsync();
            var totalDistanceKm = completedWalks.Sum(w => w.DistanceMeters) / 1000;
            var progression = await _dogProgressionService.EnsureProfileAsync(dog.Id);
            var dogLevel = _dogProgressionService.CalculateLevelInfo(progression.TotalXp);
            var recentPhotos = await _context.WalkPhotos
                .AsNoTracking()
                .Where(photo => photo.Walk != null && photo.Walk.DogId == dog.Id &&
                    photo.Walk.OwnerId == userId && photo.Walk.Status == "Completed" && photo.UserId == userId && photo.ImageUrl != "")
                .OrderByDescending(photo => photo.CreatedAt)
                .Take(3)
                .ToListAsync();
            var longestWalkId = completedWalks.OrderByDescending(walk => walk.DistanceMeters).FirstOrDefault()?.Id;
            var longestWalkPhotoUrl = longestWalkId.HasValue
                ? await _context.WalkPhotos.AsNoTracking()
                    .Where(photo => photo.WalkId == longestWalkId.Value && photo.UserId == userId && photo.ImageUrl != "")
                    .OrderByDescending(photo => photo.CreatedAt)
                    .Select(photo => photo.ImageUrl)
                    .FirstOrDefaultAsync()
                : null;

            var model = new DogDetailsViewModel
            {
                Dog = dog,
                RecentWalks = completedWalks.Take(5).ToList(),
                TotalDistanceKm = totalDistanceKm,
                CompletedWalkCount = completedWalks.Count,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                Achievements = GetDogAchievements(
                    completedWalks.Count,
                    completedWalks.Count(w => w.StartedAt >= weekStart),
                    totalDistanceKm,
                    completedWalks.Sum(w => w.UsedBinsCount),
                    parkVisits.Select(visit => visit.PlaceKey).Distinct().Count()),
                ActivityInsights = ActivityInsightsBuilder.Build(completedWalks, weeklyGoalKm: 7, monthlyGoalKm: 30),
                Progression = new DogProgressionViewModel
                {
                    TotalXp = dogLevel.TotalXp,
                    Level = dogLevel.Level,
                    XpRemaining = dogLevel.XpRemaining,
                    ProgressPercent = dogLevel.ProgressPercent,
                    DogClass = progression.DogClass,
                    Adventure = progression.Adventure,
                    Social = progression.Social,
                    Forest = progression.Forest,
                    City = progression.City,
                    Water = progression.Water,
                    Speed = progression.Speed
                },
                Memories = BuildDogMemories(completedWalks, parkVisits, recentPhotos, longestWalkPhotoUrl),
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
                ParkLocationCount = parkVisits.Select(visit => visit.PlaceKey).Distinct().Count()
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
                MapIconKey = NormalizeMapIconKey(dog.MapIconKey),
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
                MapIconKey = NormalizeMapIconKey(model.MapIconKey),
                PhotoUrl = photoUrl,
                OwnerId = userId,
                CreatedAt = DateTime.UtcNow
            };

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                _context.Dogs.Add(dog);
                await _context.SaveChangesAsync();
                await _userAchievementService.TryUnlockAsync(
                    userId,
                    UserAchievementCatalog.DogParent,
                    dog.CreatedAt,
                    nameof(Dog),
                    dog.Id.ToString());
                await _dogProgressionService.EnsureProfileAsync(dog.Id);
                await transaction.CommitAsync();
            }

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
            dog.MapIconKey = NormalizeMapIconKey(model.MapIconKey);
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
                BuildAchievement("Prvi sprehod", "Zaključi prvi sprehod.", completedWalks, 1, suffix: "sprehodov"),
                BuildAchievement("10 km skupaj", "Prehodita 10 km.", totalDistanceKm, 10, suffix: "km"),
                BuildAchievement("100 km skupaj", "Prehodita 100 km.", totalDistanceKm, 100, suffix: "km"),
                BuildAchievement("Reden teden", "Zaključita 3 sprehode ta teden.", walksThisWeek, 3, suffix: "ta teden"),
                BuildAchievement("Koši na poti", "Uporabita 5 pasjih košev med sprehodi.", usedBinsCount, 5, suffix: "uporab"),
                BuildAchievement("Pasji parki", "Obiščita 5 različnih pasjih parkov.", uniqueParkCount, 5, suffix: "parkov")
            ];
        }

        private static IReadOnlyList<DogMemoryItem> BuildDogMemories(
            IReadOnlyList<Walk> completedWalks,
            IReadOnlyList<DogParkVisit> parkVisits,
            IReadOnlyList<WalkPhoto> recentPhotos,
            string? longestWalkPhotoUrl)
        {
            var memories = new List<DogMemoryItem>();
            var bestWalk = completedWalks.OrderByDescending(walk => walk.DistanceMeters).FirstOrDefault();
            if (bestWalk != null)
            {
                memories.Add(new DogMemoryItem
                {
                    Title = "Najdaljši sprehod",
                    Description = $"Sprehod: {SlovenianFormatting.WalkDistance(bestWalk.DistanceMeters)}",
                    OccurredAt = bestWalk.StartedAt,
                    ImageUrl = longestWalkPhotoUrl
                });
            }

            foreach (var photo in recentPhotos)
            {
                memories.Add(new DogMemoryItem
                {
                    Title = "Fotografija s sprehoda",
                    Description = string.IsNullOrWhiteSpace(photo.Caption) ? "Nova fotografija s sprehoda" : photo.Caption,
                    OccurredAt = photo.CreatedAt,
                    ImageUrl = photo.ImageUrl
                });
            }

            foreach (var visit in parkVisits
                .GroupBy(item => item.PlaceKey)
                .Select(group => group.OrderByDescending(item => item.VisitedAt).First())
                .OrderByDescending(item => item.VisitedAt)
                .Take(3))
            {
                memories.Add(new DogMemoryItem
                {
                    Title = "Odkrit park",
                    Description = visit.ParkName,
                    OccurredAt = visit.VisitedAt
                });
            }

            return memories
                .OrderByDescending(item => item.OccurredAt)
                .Take(6)
                .ToList();
        }

        private static AchievementItem BuildAchievement(string name, string description, double current, double target, string suffix)
        {
            var safeTarget = Math.Max(target, 1);
            var progressPercent = (int)Math.Min(100, Math.Round(current / safeTarget * 100));
            var currentText = suffix == "km" ? current.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("sl-SI")) : Math.Floor(current).ToString("0");
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

        private static string NormalizeVisibility(string? visibility)
        {
            return visibility is "Visible" or "FriendsOnly" ? visibility : "Invisible";
        }

        private static string NormalizeMapIconKey(string? iconKey)
        {
            return iconKey is
                "dog-face" or
                "beagle" or
                "retriever" or
                "terrier" or
                "poodle" or
                "husky" or
                "bulldog" or
                "dachshund" or
                "shepherd" or
                "spaniel" or
                "shiba" or
                "dalmatian" or
                "puppy" or
                "senior" or
                "small-dog" or
                "big-dog"
                ? iconKey
                : "dog-face";
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
