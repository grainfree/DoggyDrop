using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace DoggyDrop.Controllers
{
    [Authorize]
    public class WalksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IGamificationService _gamificationService;
        private readonly IDogProgressionService _dogProgressionService;
        private readonly IOsmWalkPlannerService _osmWalkPlannerService;

        public WalksController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            ICloudinaryService cloudinaryService,
            IGamificationService gamificationService,
            IDogProgressionService dogProgressionService,
            IOsmWalkPlannerService osmWalkPlannerService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
            _gamificationService = gamificationService;
            _dogProgressionService = dogProgressionService;
            _osmWalkPlannerService = osmWalkPlannerService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? dogId)
        {
            var userId = _userManager.GetUserId(User);
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);

            var dogs = await _context.Dogs
                .Where(d => d.OwnerId == userId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            var activeWalk = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.PlannedWalk)
                .Include(w => w.StopCompletions!)
                .Include(w => w.Photos!)
                .FirstOrDefaultAsync(w => w.OwnerId == userId && w.Status == "Active");

            if (dogId.HasValue && !dogs.Any(d => d.Id == dogId.Value))
            {
                dogId = null;
            }

            var completedWalksQuery = _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.PlannedWalk)
                .Include(w => w.StopCompletions!)
                .Include(w => w.Photos!)
                .Where(w => w.OwnerId == userId && w.Status == "Completed");

            if (dogId.HasValue)
            {
                completedWalksQuery = completedWalksQuery.Where(w => w.DogId == dogId.Value);
            }

            var completedWalks = await completedWalksQuery
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();

            var recentWalks = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .Include(w => w.StopCompletions!)
                .Include(w => w.Photos!)
                .Where(w => w.OwnerId == userId && w.Status == "Completed" && (!dogId.HasValue || w.DogId == dogId.Value))
                .OrderByDescending(w => w.StartedAt)
                .Take(8)
                .ToListAsync();
            var recentPlans = await _context.PlannedWalks
                .Include(plan => plan.Dog)
                .Where(plan => plan.OwnerId == userId)
                .OrderByDescending(plan => plan.CreatedAt)
                .Take(4)
                .Select(plan => new PlannedWalkSummaryItem
                {
                    Id = plan.Id,
                    Title = plan.Title,
                    DogId = plan.DogId,
                    DogName = plan.Dog != null ? plan.Dog.Name : null,
                    AreaName = plan.AreaName,
                    TargetDistanceKm = plan.TargetDistanceKm,
                    EstimatedMinutes = plan.EstimatedMinutes,
                    CreatedAt = plan.CreatedAt,
                    UsedAt = plan.UsedAt
                })
                .ToListAsync();

            var totalDistanceKm = completedWalks.Sum(w => w.DistanceMeters) / 1000;
            var totalBinsAdded = await _context.TrashBins.CountAsync(bin => bin.UserId == userId);
            var uniqueParkVisits = await _context.DogParkVisits
                .Where(visit => visit.UserId == userId)
                .Select(visit => visit.PlaceKey)
                .Distinct()
                .CountAsync();
            var weeklyCompletedWalks = await _context.Walks
                .Include(walk => walk.Dog)
                .Include(walk => walk.Owner)
                .Where(walk => walk.Status == "Completed" && walk.StartedAt >= weekStart)
                .ToListAsync();
            var contributorWindowStart = DateTime.UtcNow.Date.AddDays(-27);
            var recentContributorBins = await _context.TrashBins
                .Include(bin => bin.User)
                .Where(bin => bin.DateAdded >= contributorWindowStart)
                .ToListAsync();

            var model = new WalksIndexViewModel
            {
                Dogs = dogs,
                ActiveWalk = activeWalk,
                RecentWalks = recentWalks,
                TotalDistanceKm = totalDistanceKm,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                CompletedWalkCount = completedWalks.Count,
                SelectedDogId = dogId,
                AverageDistanceKm = completedWalks.Count == 0 ? 0 : totalDistanceKm / completedWalks.Count,
                LongestWalkKm = completedWalks.Count == 0 ? 0 : completedWalks.Max(w => w.DistanceMeters) / 1000,
                UsedBinsCount = completedWalks.Sum(w => w.UsedBinsCount),
                WeeklyStats = BuildWeeklyStats(completedWalks),
                ActivityInsights = ActivityInsightsBuilder.Build(completedWalks),
                RecentPlans = recentPlans,
                Achievements = BuildWalkAchievements(totalBinsAdded, completedWalks.Count, totalDistanceKm, uniqueParkVisits),
                Gamification = BuildGamificationSummary(completedWalks, weeklyCompletedWalks, recentContributorBins),
                Suggestions = BuildSuggestions(dogId.HasValue
                    ? dogs.FirstOrDefault(d => d.Id == dogId.Value)
                    : dogs.FirstOrDefault()),
                QuickTemplates = BuildQuickWalkTemplates()
            };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Plan(int id)
        {
            var userId = _userManager.GetUserId(User);
            var plan = await _context.PlannedWalks
                .Include(item => item.Stops)
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId);

            if (plan == null)
            {
                return NotFound();
            }

            return RedirectToAction(nameof(Planner), new
            {
                dogId = plan.DogId,
                area = plan.AreaKey,
                distanceKm = plan.TargetDistanceKm,
                includeBins = plan.IncludeBins,
                includePark = plan.IncludePark,
                includeWater = plan.IncludeWater,
                includeDogFriendly = plan.IncludeDogFriendly,
                savedPlanId = plan.Id
            });
        }

        [HttpGet]
        public IActionResult Suggestions()
        {
            var model = BuildSuggestions(null);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Planner(
            int? dogId,
            string? area,
            double? distanceKm,
            string? walkStyle,
            string? dogEnergy,
            double? latitude,
            double? longitude,
            bool includeBins = true,
            bool includePark = true,
            bool includeWater = true,
            bool includeDogFriendly = true,
            int? savedPlanId = null)
        {
            var userId = _userManager.GetUserId(User);
            var dogs = await _context.Dogs
                .Where(d => d.OwnerId == userId)
                .OrderBy(d => d.Name)
                .ToListAsync();

            if (dogId.HasValue && !dogs.Any(dog => dog.Id == dogId.Value))
            {
                dogId = null;
            }

            var areas = GetPlannerAreas();
            var styles = GetPlannerStyles();
            var areaKey = areas.Any(candidate => candidate.Key == area)
                ? area!
                : "maribor";
            var hasCurrentLocation = IsValidPlannerCoordinate(latitude, longitude);
            var start = hasCurrentLocation
                ? new PlannerAreaCenter("Moja lokacija", latitude!.Value, longitude!.Value)
                : GetPlannerAreaCenter(areaKey);
            var safeDistanceKm = Math.Clamp(distanceKm ?? 3, 1, 12);
            var selectedWalkStyle = styles.Any(item => item.Key == walkStyle) ? walkStyle! : "balanced";
            var selectedDogEnergy = NormalizeDogEnergy(dogEnergy);

            var bins = await _context.TrashBins
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var route = hasCurrentLocation
                ? await _osmWalkPlannerService.PlanAsync(
                    start.Latitude,
                    start.Longitude,
                    safeDistanceKm,
                    bins,
                    selectedWalkStyle,
                    selectedDogEnergy,
                    includeBins,
                    includePark,
                    includeWater,
                    includeDogFriendly,
                    HttpContext.RequestAborted)
                : null;

            var model = new WalkPlannerViewModel
            {
                Dogs = dogs,
                SelectedDogId = dogId ?? dogs.FirstOrDefault()?.Id,
                AreaKey = areaKey,
                Latitude = hasCurrentLocation ? latitude : null,
                Longitude = hasCurrentLocation ? longitude : null,
                UsesCurrentLocation = hasCurrentLocation,
                StartLabel = start.Name,
                TargetDistanceKm = safeDistanceKm,
                IncludeBins = includeBins,
                IncludePark = includePark,
                IncludeWater = includeWater,
                IncludeDogFriendly = includeDogFriendly,
                Areas = areas,
                Styles = styles,
                WalkStyle = selectedWalkStyle,
                DogEnergy = selectedDogEnergy,
                Route = route ?? BuildPlannedRoute(
                    areaKey,
                    start,
                    hasCurrentLocation,
                    safeDistanceKm,
                    bins,
                    selectedWalkStyle,
                    selectedDogEnergy,
                    includeBins,
                    includePark,
                    includeWater,
                    includeDogFriendly)
            };
            if (model.Route != null && savedPlanId.HasValue)
            {
                model.Route.SavedPlanId = savedPlanId;
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(int dogId, int? plannedWalkId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var dogExists = await _context.Dogs.AnyAsync(d => d.Id == dogId && d.OwnerId == userId);
            if (!dogExists)
            {
                return NotFound();
            }

            PlannedWalk? plannedWalk = null;
            if (plannedWalkId.HasValue)
            {
                plannedWalk = await _context.PlannedWalks
                    .FirstOrDefaultAsync(plan => plan.Id == plannedWalkId.Value && plan.OwnerId == userId);

                if (plannedWalk == null)
                {
                    return NotFound();
                }

                if (plannedWalk.DogId.HasValue && plannedWalk.DogId.Value != dogId)
                {
                    return BadRequest();
                }
            }

            var hasActiveWalk = await _context.Walks.AnyAsync(w => w.OwnerId == userId && w.Status == "Active");
            if (hasActiveWalk)
            {
                TempData["ErrorMessage"] = "Najprej zakljuci trenutni sprehod.";
                return RedirectToAction(nameof(Index));
            }

            var walk = new Walk
            {
                DogId = dogId,
                OwnerId = userId,
                StartedAt = DateTime.UtcNow,
                Status = "Active",
                PlannedWalkId = plannedWalk?.Id
            };

            if (plannedWalk != null)
            {
                plannedWalk.UsedAt = DateTime.UtcNow;
            }

            _context.Walks.Add(walk);
            await _context.SaveChangesAsync();
            await NotifyFriendsAboutWalkStartAsync(walk.Id, userId, dogId, plannedWalk?.Title);

            return RedirectToAction(nameof(Active), new { id = walk.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePlan(
            int? dogId,
            string area,
            double distanceKm,
            string? walkStyle,
            string? dogEnergy,
            double? latitude,
            double? longitude,
            bool includeBins = true,
            bool includePark = true,
            bool includeWater = true,
            bool includeDogFriendly = true)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            if (dogId.HasValue)
            {
                var dogExists = await _context.Dogs.AnyAsync(dog => dog.Id == dogId.Value && dog.OwnerId == userId);
                if (!dogExists)
                {
                    return NotFound();
                }
            }

            var areaKey = GetPlannerAreas().Any(candidate => candidate.Key == area) ? area : "maribor";
            var hasCurrentLocation = IsValidPlannerCoordinate(latitude, longitude);
            var start = hasCurrentLocation
                ? new PlannerAreaCenter("Moja lokacija", latitude!.Value, longitude!.Value)
                : GetPlannerAreaCenter(areaKey);
            var safeDistanceKm = Math.Clamp(distanceKm, 1, 12);
            var selectedWalkStyle = GetPlannerStyles().Any(item => item.Key == walkStyle) ? walkStyle! : "balanced";
            var selectedDogEnergy = NormalizeDogEnergy(dogEnergy);
            var bins = await _context.TrashBins
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var route = hasCurrentLocation
                ? await _osmWalkPlannerService.PlanAsync(
                    start.Latitude,
                    start.Longitude,
                    safeDistanceKm,
                    bins,
                    selectedWalkStyle,
                    selectedDogEnergy,
                    includeBins,
                    includePark,
                    includeWater,
                    includeDogFriendly,
                    HttpContext.RequestAborted)
                : null;
            route ??= BuildPlannedRoute(areaKey, start, hasCurrentLocation, safeDistanceKm, bins, selectedWalkStyle, selectedDogEnergy, includeBins, includePark, includeWater, includeDogFriendly);

            var plan = new PlannedWalk
            {
                OwnerId = userId,
                DogId = dogId,
                Title = route.Title,
                AreaKey = hasCurrentLocation ? "current-location" : areaKey,
                AreaName = start.Name,
                TargetDistanceKm = safeDistanceKm,
                EstimatedDistanceKm = route.EstimatedDistanceKm,
                EstimatedMinutes = route.EstimatedMinutes,
                IncludeBins = includeBins,
                IncludePark = includePark,
                IncludeWater = includeWater,
                IncludeDogFriendly = includeDogFriendly,
                CreatedAt = DateTime.UtcNow,
                Stops = route.Stops.Select(stop => new PlannedWalkStop
                {
                    Order = stop.Order,
                    Name = stop.Name,
                    Type = stop.Type,
                    Label = stop.Label,
                    Reason = stop.Reason,
                    Latitude = stop.Latitude,
                    Longitude = stop.Longitude
                }).ToList(),
                RoutePoints = route.RoutePoints.Select((point, index) => new PlannedWalkRoutePoint
                {
                    Order = index + 1,
                    Latitude = point.Latitude,
                    Longitude = point.Longitude
                }).ToList()
            };

            _context.PlannedWalks.Add(plan);
            await _context.SaveChangesAsync();
            await _gamificationService.AwardXpAsync(
                userId,
                GamificationConstants.CreateRoute,
                GamificationConstants.CreateRouteXp,
                nameof(PlannedWalk),
                plan.Id.ToString(),
                "Nova pot");
            await _gamificationService.RecordStreakActivityAsync(userId, GamificationStreakConstants.Explorer);

            TempData["SuccessMessage"] = "Plan sprehoda je shranjen.";
            return RedirectToAction(nameof(Planner), new
            {
                dogId,
                area = areaKey,
                latitude = hasCurrentLocation ? latitude : null,
                longitude = hasCurrentLocation ? longitude : null,
                distanceKm = safeDistanceKm,
                walkStyle = selectedWalkStyle,
                dogEnergy = selectedDogEnergy,
                includeBins,
                includePark,
                includeWater,
                includeDogFriendly,
                savedPlanId = plan.Id
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePlan(int id)
        {
            var userId = _userManager.GetUserId(User);
            var plan = await _context.PlannedWalks
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId);

            if (plan == null)
            {
                return NotFound();
            }

            var isUsed = await _context.Walks.AnyAsync(walk => walk.PlannedWalkId == plan.Id);
            if (isUsed)
            {
                plan.UsedAt ??= DateTime.UtcNow;
                TempData["ErrorMessage"] = "Plan je ze povezan s sprehodom, zato ga ne brisem.";
            }
            else
            {
                _context.PlannedWalks.Remove(plan);
                TempData["SuccessMessage"] = "Plan je izbrisan.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Active(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.Points)
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.RoutePoints)
                .Include(w => w.StopCompletions!)
                    .ThenInclude(completion => completion.PlannedWalkStop)
                .Include(w => w.Photos!)
                .FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);

            if (walk == null)
            {
                return NotFound();
            }

            return View(walk);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.Points)
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .Include(w => w.StopCompletions!)
                    .ThenInclude(completion => completion.PlannedWalkStop)
                .Include(w => w.Reactions!)
                    .ThenInclude(reaction => reaction.User)
                .Include(w => w.Comments!.Where(comment => !comment.IsDeleted))
                    .ThenInclude(comment => comment.User)
                .Include(w => w.Photos!)
                    .ThenInclude(photo => photo.Reactions)
                .Include(w => w.Photos!)
                    .ThenInclude(photo => photo.PlannedWalkStop)
                .FirstOrDefaultAsync(w => w.Id == id && (w.OwnerId == userId || w.Status == "Completed"));

            if (walk == null)
            {
                return NotFound();
            }

            if (walk.Status == "Active")
            {
                return RedirectToAction(nameof(Active), new { id = walk.Id });
            }

            ViewBag.WalkStory = BuildWalkStory(walk);
            return View(walk);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPhoto(int id, IFormFile? photo, string? caption, int? plannedWalkStopId = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(item => item.Photos!)
                .Include(item => item.PlannedWalk!)
                    .ThenInclude(plan => plan.Stops)
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId && (item.Status == "Completed" || item.Status == "Active"));

            if (walk == null)
            {
                return NotFound();
            }

            if (photo == null || photo.Length == 0)
            {
                TempData["ErrorMessage"] = "Fotografija ni bila izbrana.";
                return RedirectToPhotoSource(walk);
            }

            var imageUrl = await _cloudinaryService.UploadWalkImageAsync(photo);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                TempData["ErrorMessage"] = "Fotografije ni bilo mogoce shraniti.";
                return RedirectToPhotoSource(walk);
            }

            if (plannedWalkStopId.HasValue)
            {
                var hasStop = walk.PlannedWalk?.Stops?.Any(stop => stop.Id == plannedWalkStopId.Value) ?? false;
                if (!hasStop)
                {
                    TempData["ErrorMessage"] = "Izbran postanek za fotografijo ni veljaven.";
                    return RedirectToPhotoSource(walk);
                }
            }

            var walkPhoto = new WalkPhoto
            {
                WalkId = walk.Id,
                UserId = userId,
                ImageUrl = imageUrl,
                Caption = TrimToLength(caption, 120),
                CreatedAt = DateTime.UtcNow,
                PlannedWalkStopId = plannedWalkStopId
            };

            _context.WalkPhotos.Add(walkPhoto);
            await _context.SaveChangesAsync();
            await _gamificationService.AwardXpAsync(
                userId,
                GamificationConstants.UploadPhoto,
                GamificationConstants.UploadPhotoXp,
                nameof(WalkPhoto),
                walkPhoto.Id.ToString(),
                "Nalozena fotografija");
            await _gamificationService.RecordStreakActivityAsync(userId, GamificationStreakConstants.Contribution);
            await _dogProgressionService.AwardXpAsync(
                walk.DogId,
                "WalkPhoto",
                12,
                new DogProgressionStatBoost { Social = 6, Adventure = 3 },
                nameof(WalkPhoto),
                walkPhoto.Id.ToString(),
                "Fotografija s sprehoda");

            TempData["SuccessMessage"] = "Fotografija sprehoda je dodana.";
            return RedirectToPhotoSource(walk);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePhoto(int id, int photoId)
        {
            var userId = _userManager.GetUserId(User);
            var photo = await _context.WalkPhotos
                .Include(item => item.Walk)
                .FirstOrDefaultAsync(item => item.Id == photoId && item.WalkId == id && item.UserId == userId);

            if (photo == null)
            {
                return NotFound();
            }

            _context.WalkPhotos.Remove(photo);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Fotografija je odstranjena.";
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePhotoReaction(int id, int photoId, string? reactionType = null, string? returnUrl = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var photo = await _context.WalkPhotos
                .Include(item => item.Walk)
                .FirstOrDefaultAsync(item => item.Id == photoId && item.WalkId == id);

            if (photo?.Walk == null || photo.Walk.Status != "Completed")
            {
                return NotFound();
            }

            var normalizedReaction = NormalizeReactionType(reactionType, "heart");
            var existingReaction = await _context.WalkPhotoReactions
                .FirstOrDefaultAsync(reaction => reaction.WalkPhotoId == photoId && reaction.UserId == userId);

            if (existingReaction == null)
            {
                _context.WalkPhotoReactions.Add(new WalkPhotoReaction
                {
                    WalkPhotoId = photoId,
                    UserId = userId,
                    ReactionType = normalizedReaction,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (existingReaction.ReactionType != normalizedReaction)
            {
                existingReaction.ReactionType = normalizedReaction;
                existingReaction.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.WalkPhotoReactions.Remove(existingReaction);
            }

            await _context.SaveChangesAsync();
            return RedirectToLocalOrDetails(returnUrl, id);
        }

        [HttpGet]
        public async Task<IActionResult> ShareCard(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.Points)
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .Include(w => w.StopCompletions!)
                    .ThenInclude(completion => completion.PlannedWalkStop)
                .Include(w => w.Photos!)
                .FirstOrDefaultAsync(w => w.Id == id && (w.OwnerId == userId || w.Status == "Completed"));

            if (walk == null)
            {
                return NotFound();
            }

            var svg = BuildShareCardSvg(walk);
            return File(Encoding.UTF8.GetBytes(svg), "image/svg+xml", $"doggydrop-walk-{walk.Id}.svg");
        }

        [HttpGet]
        public IActionResult Share(int id)
        {
            return RedirectToAction(nameof(Details), new { id, share = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLike(int id, string? reactionType = null, string? returnUrl = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(w => w.Dog)
                .FirstOrDefaultAsync(w => w.Id == id && w.Status == "Completed");

            if (walk == null)
            {
                return NotFound();
            }

            var normalizedReaction = NormalizeReactionType(reactionType, "paw");
            var existingReaction = await _context.WalkReactions
                .FirstOrDefaultAsync(reaction => reaction.WalkId == id && reaction.UserId == userId);

            if (existingReaction == null)
            {
                _context.WalkReactions.Add(new WalkReaction
                {
                    WalkId = id,
                    UserId = userId,
                    ReactionType = normalizedReaction,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                if (walk.OwnerId != userId)
                {
                    var currentUser = await _userManager.GetUserAsync(User);
                    var displayName = GetDisplayName(currentUser);
                    await _notificationService.CreateAsync(
                        walk.OwnerId,
                        "WalkReaction",
                        "Nova reakcija na sprehodu",
                        $"{displayName} je reagiral na sprehod psa {walk.Dog?.Name ?? "Pes"}: {GetReactionLabel(normalizedReaction)}.",
                        Url.Action(nameof(Details), "Walks", new { id = walk.Id }));
                }
            }
            else if (existingReaction.ReactionType != normalizedReaction)
            {
                existingReaction.ReactionType = normalizedReaction;
                existingReaction.CreatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            else
            {
                _context.WalkReactions.Remove(existingReaction);
                await _context.SaveChangesAsync();
            }

            return RedirectToLocalOrDetails(returnUrl, id);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddComment(int id, string? body, string? returnUrl = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(w => w.Dog)
                .FirstOrDefaultAsync(w => w.Id == id && w.Status == "Completed");

            if (walk == null)
            {
                return NotFound();
            }

            var trimmedBody = body?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedBody))
            {
                TempData["ErrorMessage"] = "Komentar ne sme biti prazen.";
                return RedirectToLocalOrDetails(returnUrl, id);
            }

            if (trimmedBody.Length > 240)
            {
                trimmedBody = trimmedBody[..240];
            }

            _context.WalkComments.Add(new WalkComment
            {
                WalkId = id,
                UserId = userId,
                Body = trimmedBody,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            if (walk.OwnerId != userId)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                var displayName = GetDisplayName(currentUser);
                await _notificationService.CreateAsync(
                    walk.OwnerId,
                    "WalkComment",
                    "Nov komentar",
                    $"{displayName} je komentiral sprehod psa {walk.Dog?.Name ?? "Pes"}.",
                    Url.Action(nameof(Details), "Walks", new { id = walk.Id }));
            }

            TempData["SuccessMessage"] = "Komentar je dodan.";
            return RedirectToLocalOrDetails(returnUrl, id);
        }

        [HttpPost]
        public async Task<IActionResult> AddPoint(int id, [FromBody] WalkPointInput input)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(w => w.Points)
                .FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId && w.Status == "Active");

            if (walk == null)
            {
                return NotFound();
            }

            if (input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180)
            {
                return BadRequest("Invalid coordinates.");
            }

            var recordedAt = input.RecordedAt ?? DateTime.UtcNow;
            var lastPoint = walk.Points?
                .OrderByDescending(point => point.RecordedAt)
                .FirstOrDefault();

            if (lastPoint != null)
            {
                walk.DistanceMeters += GetDistanceMeters(
                    lastPoint.Latitude,
                    lastPoint.Longitude,
                    input.Latitude,
                    input.Longitude);
            }

            var point = new WalkPoint
            {
                WalkId = walk.Id,
                Latitude = input.Latitude,
                Longitude = input.Longitude,
                RecordedAt = recordedAt
            };

            _context.WalkPoints.Add(point);
            await _context.SaveChangesAsync();

            return Json(new
            {
                walk.DistanceMeters,
                pointCount = (walk.Points?.Count ?? 0) + 1
            });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleStop(int id, int stopId, [FromBody] ToggleStopInput? input = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId && w.Status == "Active");

            if (walk == null || walk.PlannedWalk == null)
            {
                return NotFound();
            }

            var stopExists = walk.PlannedWalk.Stops?.Any(stop => stop.Id == stopId) ?? false;
            if (!stopExists)
            {
                return NotFound();
            }

            var existing = await _context.WalkStopCompletions
                .FirstOrDefaultAsync(completion => completion.WalkId == id && completion.PlannedWalkStopId == stopId);
            var shouldComplete = input?.Completed ?? existing == null;

            if (shouldComplete && existing == null)
            {
                _context.WalkStopCompletions.Add(new WalkStopCompletion
                {
                    WalkId = id,
                    PlannedWalkStopId = stopId,
                    UserId = userId,
                    CompletedAt = DateTime.UtcNow
                });
            }
            else if (!shouldComplete && existing != null)
            {
                _context.WalkStopCompletions.Remove(existing);
            }

            await _context.SaveChangesAsync();
            var completedCount = await _context.WalkStopCompletions.CountAsync(completion => completion.WalkId == id);
            var totalCount = walk.PlannedWalk.Stops?.Count ?? 0;

            return Json(new
            {
                Completed = shouldComplete,
                CompletedCount = completedCount,
                TotalCount = totalCount
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finish(int id, string? manualDistanceKm, int? usedBinsCount)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks.FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);

            if (walk == null)
            {
                return NotFound();
            }

            var completedNow = walk.Status == "Active";
            var isFirstCompletedWalk = false;
            if (completedNow)
            {
                if (TryParseDistanceKm(manualDistanceKm, out var parsedDistanceKm))
                {
                    walk.DistanceMeters = parsedDistanceKm * 1000;
                }

                if (usedBinsCount.HasValue)
                {
                    walk.UsedBinsCount = Math.Clamp(usedBinsCount.Value, 0, 50);
                }

                walk.EndedAt = DateTime.UtcNow;
                walk.Status = "Completed";
                isFirstCompletedWalk = await _context.Walks
                    .CountAsync(candidate => candidate.OwnerId == userId && candidate.Status == "Completed") == 0;
            }

            await _context.SaveChangesAsync();
            if (completedNow)
            {
                await NotifyWalkAchievementsAsync(userId!, walk.DistanceMeters / 1000);
                var distanceXp = (int)Math.Floor(walk.DistanceMeters / 1000d) * GamificationConstants.WalkDistanceXpPerKm;
                await _gamificationService.AwardXpAsync(
                    userId,
                    GamificationConstants.WalkDistance,
                    distanceXp,
                    nameof(Walk),
                    walk.Id.ToString(),
                    "Zakljucen sprehod");
                await _gamificationService.RecordStreakActivityAsync(userId, GamificationStreakConstants.Walk);
                await _dogProgressionService.AwardXpAsync(
                    walk.DogId,
                    "CompletedWalk",
                    Math.Max(10, (int)Math.Round(walk.DistanceMeters / 1000d * 18)),
                    BuildDogWalkStats(walk),
                    nameof(Walk),
                    walk.Id.ToString(),
                    "Zakljucen sprehod");
            }

            TempData["SuccessMessage"] = "Sprehod je shranjen.";
            if (completedNow && isFirstCompletedWalk)
            {
                TempData["ShowFirstWalkCelebration"] = true;
                TempData["FirstWalkDogName"] = walk.DogId.ToString();
                TempData["FirstWalkDistanceKm"] = (walk.DistanceMeters / 1000d).ToString("0.00", CultureInfo.InvariantCulture);
            }

            return completedNow
                ? RedirectToAction(nameof(Details), new { id })
                : RedirectToAction(nameof(Index));
        }

        private async Task NotifyFriendsAboutWalkStartAsync(int walkId, string userId, int dogId, string? plannedWalkTitle)
        {
            var dog = await _context.Dogs
                .Where(item => item.Id == dogId && item.OwnerId == userId)
                .Select(item => new { item.Name })
                .FirstOrDefaultAsync();

            var currentUser = await _userManager.GetUserAsync(User);
            var displayName = GetDisplayName(currentUser);
            var friendIds = await _context.Friendships
                .Where(friendship => friendship.Status == "Accepted" &&
                    (friendship.RequesterId == userId || friendship.AddresseeId == userId))
                .Select(friendship => friendship.RequesterId == userId ? friendship.AddresseeId : friendship.RequesterId)
                .Distinct()
                .ToListAsync();

            var routeHint = string.IsNullOrWhiteSpace(plannedWalkTitle)
                ? "Sprehod se je pravkar zacel."
                : $"Plan: {plannedWalkTitle}.";

            foreach (var friendId in friendIds)
            {
                await _notificationService.CreateUniqueRecentAsync(
                    friendId,
                    "FriendStartedWalk",
                    "Prijatelj je zacel sprehod",
                    $"{displayName} je zacel sprehod s psom {dog?.Name ?? "Pes"}. {routeHint}",
                    Url.Action("Community", "Home"),
                    withinHours: 2);
            }
        }

        private static DogProgressionStatBoost BuildDogWalkStats(Walk walk)
        {
            var distanceKm = walk.DistanceMeters / 1000d;
            var durationHours = walk.EndedAt.HasValue
                ? Math.Max(0.05, (walk.EndedAt.Value - walk.StartedAt).TotalHours)
                : 0.5;
            var speedKmh = distanceKm / durationHours;

            return new DogProgressionStatBoost
            {
                Adventure = Math.Max(1, (int)Math.Round(distanceKm * 8)),
                City = walk.UsedBinsCount > 0 ? Math.Min(20, walk.UsedBinsCount * 4) : 2,
                Speed = speedKmh >= 6 ? Math.Min(30, (int)Math.Round(speedKmh * 3)) : 0
            };
        }

        private static string NormalizeReactionType(string? reactionType, string fallback)
        {
            return reactionType?.Trim().ToLowerInvariant() switch
            {
                "paw" => "paw",
                "heart" => "heart",
                "fire" => "fire",
                "good-route" => "good-route",
                "cute-dog" => "cute-dog",
                _ => fallback
            };
        }

        private static string GetReactionLabel(string reactionType)
        {
            return reactionType switch
            {
                "heart" => "srcek",
                "fire" => "ogenj",
                "good-route" => "good route",
                "cute-dog" => "cute dog",
                _ => "tacka"
            };
        }

        private async Task NotifyWalkAchievementsAsync(string userId, double lastWalkDistanceKm)
        {
            var totalWalkCount = await _context.Walks
                .CountAsync(w => w.OwnerId == userId && w.Status == "Completed");
            var totalDistanceKm = await _context.Walks
                .Where(w => w.OwnerId == userId && w.Status == "Completed")
                .SumAsync(w => w.DistanceMeters) / 1000;
            var previousDistanceKm = Math.Max(0, totalDistanceKm - lastWalkDistanceKm);

            if (totalWalkCount == 1)
            {
                await _notificationService.CreateUniqueRecentAsync(
                    userId,
                    "Achievement",
                    "First walk",
                    "Zakljucil si svoj prvi DoggyDrop sprehod.",
                    Url.Action("UserProfile", "Home"),
                    withinHours: 24 * 365);
            }

            foreach (var target in new[] { 10, 100 })
            {
                if (previousDistanceKm < target && totalDistanceKm >= target)
                {
                    await _notificationService.CreateUniqueRecentAsync(
                        userId,
                        "Achievement",
                        $"{target} km walked",
                        $"Skupaj si dosegel {target} km sprehodov v DoggyDrop.",
                        Url.Action("UserProfile", "Home"),
                        withinHours: 24 * 365);
                }
            }
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

        private static double GetDistanceKm(double lat1, double lng1, double lat2, double lng2)
        {
            return GetDistanceMeters(lat1, lng1, lat2, lng2) / 1000;
        }

        private static double ToRadians(double value)
        {
            return value * Math.PI / 180;
        }

        private static bool TryParseDistanceKm(string? input, out double distanceKm)
        {
            distanceKm = 0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var normalized = input.Trim().Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            if (parsed < 0 || parsed > 100)
            {
                return false;
            }

            distanceKm = parsed;
            return true;
        }

        private IActionResult RedirectToLocalOrDetails(string? returnUrl, int walkId)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction(nameof(Details), new { id = walkId });
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? "DoggyDrop uporabnik";
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

        private IActionResult RedirectToPhotoSource(Walk walk)
        {
            return walk.Status == "Active"
                ? RedirectToAction(nameof(Active), new { id = walk.Id })
                : RedirectToAction(nameof(Details), new { id = walk.Id });
        }

        private static string BuildWalkStory(Walk walk)
        {
            var dogName = walk.Dog?.Name ?? "Pes";
            var distanceKm = walk.DistanceMeters / 1000;
            var duration = walk.EndedAt.HasValue ? walk.EndedAt.Value - walk.StartedAt : TimeSpan.Zero;
            var completedStops = (walk.StopCompletions ?? [])
                .Where(completion => completion.PlannedWalkStop != null)
                .Select(completion => completion.PlannedWalkStop!)
                .ToList();
            var completedStopNames = completedStops
                .Select(stop => stop.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .Take(2)
                .ToList();
            var exploredCount = completedStops
                .Select(stop => stop.Id)
                .Distinct()
                .Count();
            var routeName = walk.PlannedWalk?.AreaName ?? walk.PlannedWalk?.Title;
            var routePart = !string.IsNullOrWhiteSpace(routeName)
                ? $" skozi {routeName}"
                : string.Empty;
            var photoPart = (walk.Photos?.Count ?? 0) > 0
                ? $" in ujel {walk.Photos!.Count} walk photo"
                : string.Empty;
            var binPart = walk.UsedBinsCount > 0
                ? $" Ob poti je uporabil {walk.UsedBinsCount} DoggyDrop koš{(walk.UsedBinsCount == 1 ? "" : "e")}."
                : string.Empty;
            var stopPart = completedStopNames.Count > 0
                ? $" Najboljši postanki: {string.Join(", ", completedStopNames)}."
                : exploredCount > 0
                    ? $" Raziskal je {exploredCount} planiranih postankov."
                    : string.Empty;
            var durationPart = duration.TotalMinutes >= 1
                ? $" v {duration:hh\\:mm}"
                : string.Empty;

            return $"{dogName} je danes prehodil {distanceKm:0.0} km{durationPart}{routePart}{photoPart}.{stopPart}{binPart}".Trim();
        }

        private static string BuildShareCardSvg(Walk walk)
        {
            var dogName = EscapeSvg(walk.Dog?.Name ?? "Pes");
            var distanceKm = (walk.DistanceMeters / 1000).ToString("0.00", CultureInfo.InvariantCulture);
            var duration = walk.EndedAt.HasValue
                ? (walk.EndedAt.Value - walk.StartedAt).ToString(@"hh\:mm")
                : "00:00";
            var bins = walk.UsedBinsCount.ToString(CultureInfo.InvariantCulture);
            var dateText = walk.StartedAt.ToLocalTime().ToString("dd.MM.yyyy");
            var storyText = EscapeSvg(BuildWalkStory(walk));
            var photoUrls = (walk.Photos ?? [])
                .OrderByDescending(photo => photo.CreatedAt)
                .Select(photo => photo.ImageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Take(3)
                .ToList();

            if (photoUrls.Count == 0 && !string.IsNullOrWhiteSpace(walk.Dog?.PhotoUrl))
            {
                photoUrls.Add(walk.Dog.PhotoUrl!);
            }

            var safePhotoUrls = photoUrls.Select(EscapeSvg).ToList();
            var photoLayout = BuildSharePhotoLayout(safePhotoUrls);

            return $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1080" height="1920" viewBox="0 0 1080 1920">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0%" stop-color="#25594e"/>
      <stop offset="55%" stop-color="#2f6d5f"/>
      <stop offset="100%" stop-color="#e18d32"/>
    </linearGradient>
    <linearGradient id="photoFade" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="rgba(0,0,0,0)"/>
      <stop offset="100%" stop-color="rgba(0,0,0,0.34)"/>
    </linearGradient>
  </defs>
  <rect width="1080" height="1920" fill="url(#bg)"/>
  {{photoLayout}}
  <rect x="80" y="150" width="920" height="760" rx="36" ry="36" fill="url(#photoFade)"/>
  <rect x="80" y="150" width="920" height="760" rx="36" ry="36" fill="none" stroke="rgba(255,255,255,0.18)"/>
  <rect x="80" y="980" width="920" height="770" rx="48" ry="48" fill="rgba(9,18,16,0.26)"/>
  <rect x="712" y="96" width="288" height="64" rx="32" ry="32" fill="rgba(9,18,16,0.34)" stroke="rgba(255,255,255,0.18)"/>
  <circle cx="752" cy="128" r="10" fill="#ef8f42"/>
  <text x="780" y="138" fill="rgba(255,255,255,0.92)" font-size="30" font-family="Arial, sans-serif" font-weight="800">doggydrop.app</text>
  <text x="120" y="1060" fill="#ffffff" font-size="42" font-family="Arial, sans-serif" font-weight="700">DoggyDrop walk</text>
  <text x="120" y="1150" fill="#ffffff" font-size="96" font-family="Arial, sans-serif" font-weight="800">{{dogName}}</text>
  <text x="120" y="1275" fill="#ffffff" font-size="168" font-family="Arial, sans-serif" font-weight="900">{{distanceKm}} km</text>
  <text x="120" y="1354" fill="rgba(255,255,255,0.86)" font-size="38" font-family="Arial, sans-serif" font-weight="700">
    {{BuildSvgTextLines(storyText, 58, 3, 120, 1354)}}
  </text>
  <rect x="120" y="1435" width="260" height="190" rx="32" ry="32" fill="rgba(255,255,255,0.12)"/>
  <rect x="410" y="1435" width="260" height="190" rx="32" ry="32" fill="rgba(255,255,255,0.12)"/>
  <rect x="700" y="1435" width="260" height="190" rx="32" ry="32" fill="rgba(255,255,255,0.12)"/>
  <text x="150" y="1505" fill="rgba(255,255,255,0.72)" font-size="30" font-family="Arial, sans-serif">Trajanje</text>
  <text x="150" y="1578" fill="#ffffff" font-size="68" font-family="Arial, sans-serif" font-weight="800">{{duration}}</text>
  <text x="440" y="1505" fill="rgba(255,255,255,0.72)" font-size="30" font-family="Arial, sans-serif">Kosi</text>
  <text x="440" y="1578" fill="#ffffff" font-size="68" font-family="Arial, sans-serif" font-weight="800">{{bins}}</text>
  <text x="730" y="1505" fill="rgba(255,255,255,0.72)" font-size="30" font-family="Arial, sans-serif">Datum</text>
  <text x="730" y="1578" fill="#ffffff" font-size="46" font-family="Arial, sans-serif" font-weight="800">{{EscapeSvg(dateText)}}</text>
</svg>
""";
        }

        private static string BuildSvgTextLines(string text, int maxChars, int maxLines, int x, int y)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var current = new StringBuilder();

            foreach (var word in words)
            {
                if (current.Length > 0 && current.Length + word.Length + 1 > maxChars)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                if (lines.Count >= maxLines)
                {
                    break;
                }

                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(word);
            }

            if (current.Length > 0 && lines.Count < maxLines)
            {
                lines.Add(current.ToString());
            }

            return string.Join(Environment.NewLine, lines.Select((line, index) =>
                $"<tspan x=\"{x}\" y=\"{y + (index * 48)}\">{line}</tspan>"));
        }


        private static string BuildSharePhotoLayout(IReadOnlyList<string> safePhotoUrls)
        {
            if (safePhotoUrls.Count == 0)
            {
                return string.Empty;
            }

            if (safePhotoUrls.Count == 1)
            {
                return $"<image href=\"{safePhotoUrls[0]}\" x=\"80\" y=\"150\" width=\"920\" height=\"760\" preserveAspectRatio=\"xMidYMid slice\" opacity=\"0.96\" clip-path=\"inset(0 round 36px)\"/>";
            }

            if (safePhotoUrls.Count == 2)
            {
                return $"""
<image href="{safePhotoUrls[0]}" x="80" y="150" width="452" height="760" preserveAspectRatio="xMidYMid slice" opacity="0.96" clip-path="inset(0 round 36px 0 36px)"/>
<image href="{safePhotoUrls[1]}" x="548" y="150" width="452" height="760" preserveAspectRatio="xMidYMid slice" opacity="0.96" clip-path="inset(0 round 0 36px 36px 0)"/>
""";
            }

            return $"""
<image href="{safePhotoUrls[0]}" x="80" y="150" width="452" height="760" preserveAspectRatio="xMidYMid slice" opacity="0.96" clip-path="inset(0 round 36px 0 36px)"/>
<image href="{safePhotoUrls[1]}" x="548" y="150" width="452" height="372" preserveAspectRatio="xMidYMid slice" opacity="0.96" clip-path="inset(0 round 0 36px 0 0)"/>
<image href="{safePhotoUrls[2]}" x="548" y="538" width="452" height="372" preserveAspectRatio="xMidYMid slice" opacity="0.96" clip-path="inset(0 round 0 0 36px 0)"/>
""";
        }

        private static string EscapeSvg(string input)
        {
            return input
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static IReadOnlyList<WeeklyWalkStat> BuildWeeklyStats(IReadOnlyList<Walk> walks)
        {
            var today = DateTime.UtcNow.Date;
            var days = Enumerable.Range(0, 7)
                .Select(offset => today.AddDays(offset - 6))
                .ToList();

            var daily = days
                .Select(day =>
                {
                    var dayWalks = walks.Where(w => w.StartedAt.Date == day).ToList();
                    return new WeeklyWalkStat
                    {
                        DayLabel = day.ToLocalTime().ToString("ddd", new CultureInfo("sl-SI")),
                        WalkCount = dayWalks.Count,
                        DistanceKm = dayWalks.Sum(w => w.DistanceMeters) / 1000
                    };
                })
                .ToList();

            var maxDistance = Math.Max(daily.Max(d => d.DistanceKm), 0.1);
            foreach (var day in daily)
            {
                day.IntensityPercent = (int)Math.Round(day.DistanceKm / maxDistance * 100);
            }

            return daily;
        }

        private static IReadOnlyList<WalkSuggestionItem> BuildSuggestions(Dog? dog)
        {
            var bestFor = string.IsNullOrWhiteSpace(dog?.Name)
                ? "sproscen sprehod"
                : $"{dog.Name}";

            return
            [
                new WalkSuggestionItem
                {
                    Title = "Mestni krog z vodo",
                    Area = "Maribor center",
                    Description = "Kratek mestni krog mimo parka, pitnika in dog-friendly postanka.",
                    DistanceKm = 2.4,
                    Difficulty = "Lahko",
                    BestFor = bestFor
                },
                new WalkSuggestionItem
                {
                    Title = "Park + social sniff",
                    Area = "Tivoli / park",
                    Description = "Primeren za miren sprehod, srecanja z drugimi psi in pocasnejse raziskovanje.",
                    DistanceKm = 3.2,
                    Difficulty = "Srednje",
                    BestFor = bestFor
                },
                new WalkSuggestionItem
                {
                    Title = "Trail master mini",
                    Area = "Rob mesta",
                    Description = "Daljsi sprehod za aktivne pse, z vec prostora in manj mestnega hrupa.",
                    DistanceKm = 5.8,
                    Difficulty = "Aktivno",
                    BestFor = bestFor
                }
            ];
        }

        private static IReadOnlyList<WalkPlannerArea> GetPlannerAreas()
        {
            return
            [
                new WalkPlannerArea { Key = "maribor", Name = "Maribor" },
                new WalkPlannerArea { Key = "ljubljana", Name = "Ljubljana" },
                new WalkPlannerArea { Key = "koper", Name = "Koper / Obala" },
                new WalkPlannerArea { Key = "celje", Name = "Celje" },
                new WalkPlannerArea { Key = "kranj", Name = "Kranj" }
            ];
        }

        private static IReadOnlyList<PlannerStyleOption> GetPlannerStyles()
        {
            return
            [
                new PlannerStyleOption { Key = "balanced", Name = "Balanced", Description = "Malo vsega: kos, park, voda in lep krog." },
                new PlannerStyleOption { Key = "quick", Name = "Quick walk", Description = "Kratek prakticen sprehod s poudarkom na kosu." },
                new PlannerStyleOption { Key = "park", Name = "Park walk", Description = "Vec vohanja, pocasnejsi tempo in daljsi park stop." },
                new PlannerStyleOption { Key = "city", Name = "City walk", Description = "Mestni krog z vodo in dog-friendly postankom." },
                new PlannerStyleOption { Key = "long", Name = "Long walk", Description = "Daljsa trasa z rezervnim kosom in dodatnimi stopi." }
            ];
        }

        private static PlannedWalkRoute BuildPlannedRoute(
            string areaKey,
            PlannerAreaCenter area,
            bool usesCurrentLocation,
            double targetDistanceKm,
            IReadOnlyList<TrashBin> bins,
            string walkStyle,
            string dogEnergy,
            bool includeBins,
            bool includePark,
            bool includeWater,
            bool includeDogFriendly)
        {
            var effectiveDistanceKm = AdjustDistanceForEnergyAndStyle(targetDistanceKm, dogEnergy, walkStyle);
            var styleBins = includeBins;
            var stylePark = includePark;
            var styleWater = includeWater;
            var styleDogFriendly = includeDogFriendly;

            switch (walkStyle)
            {
                case "quick":
                    styleBins = true;
                    stylePark = false;
                    styleWater = effectiveDistanceKm >= 2.4 && includeWater;
                    styleDogFriendly = false;
                    break;
                case "park":
                    stylePark = true;
                    styleWater = includeWater;
                    styleDogFriendly = effectiveDistanceKm >= 4.5 && includeDogFriendly;
                    break;
                case "city":
                    styleBins = includeBins;
                    stylePark = effectiveDistanceKm >= 2.8 && includePark;
                    styleWater = true;
                    styleDogFriendly = true;
                    break;
                case "long":
                    styleBins = includeBins;
                    stylePark = includePark;
                    styleWater = includeWater;
                    styleDogFriendly = includeDogFriendly;
                    break;
            }

            var stops = new List<PlannedWalkRouteStop>
            {
                new()
                {
                    Name = $"Start: {area.Name}",
                    Type = "start",
                    Label = "Start",
                    Reason = "Zacetna tocka kroga.",
                    Latitude = area.Latitude,
                    Longitude = area.Longitude,
                    Order = 1
                }
            };

            var order = 2;
            if (styleBins)
            {
                var binsToTake = walkStyle == "long" || effectiveDistanceKm >= 5.5 ? 2 : 1;
                foreach (var bin in bins
                    .Select(bin => new
                    {
                        Bin = bin,
                        DistanceKm = GetDistanceKm(area.Latitude, area.Longitude, bin.Latitude, bin.Longitude)
                    })
                    .Where(item => item.DistanceKm <= Math.Max(2.5, effectiveDistanceKm * 1.2))
                    .OrderBy(item => item.DistanceKm)
                    .ThenByDescending(item => GetBinReliabilityScore(item.Bin))
                    .Take(binsToTake))
                {
                    stops.Add(new PlannedWalkRouteStop
                    {
                        Name = bin.Bin.Name,
                        Type = "bin",
                        Label = "Pasji kos",
                        Reason = order == 2
                            ? $"Zgodnji postanek za odlaganje iztrebka. Zanesljivost: {GetBinReliabilityLabel(bin.Bin)}."
                            : "Rezervni kos za daljsi sprehod.",
                        Latitude = bin.Bin.Latitude,
                        Longitude = bin.Bin.Longitude,
                        Order = order++
                    });
                }
            }

            var places = GetPlannerPlaces(areaKey);
            if (usesCurrentLocation)
            {
                places = [];
            }
            if (stylePark && effectiveDistanceKm >= 2.2)
            {
                AddNearestPlannerPlace(stops, places, "park", "Pasji park", "Prostor za pocasnejsi tempo, vohanje in socialni del sprehoda.", ref order);
            }

            if (styleWater && effectiveDistanceKm >= 2)
            {
                AddNearestPlannerPlace(stops, places, "water", "Voda", "Postanek za hidracijo, posebej uporaben v toplejsih dneh.", ref order);
            }

            if (styleDogFriendly && effectiveDistanceKm >= 3.2)
            {
                AddNearestPlannerPlace(stops, places, "shop", "Pet shop", "Daljsi sprehod lahko vkljuci hiter postanek za priboljske ali vrecke.", ref order);
                AddNearestPlannerPlace(stops, places, "cafe", "Dog friendly", walkStyle == "city"
                    ? "Mestni sprehod z vmesnim socialnim postankom."
                    : "Mirnejsi zakljucek ali socialni postanek.", ref order);
            }

            stops.Add(new PlannedWalkRouteStop
            {
                Name = $"Cilj: {area.Name}",
                Type = "finish",
                Label = "Cilj",
                Reason = "Zakljucek krozne poti.",
                Latitude = area.Latitude,
                Longitude = area.Longitude,
                Order = order
            });

            var routePoints = BuildLoopPoints(area.Latitude, area.Longitude, effectiveDistanceKm, stops);
            var estimatedDistanceKm = EstimateRouteDistance(routePoints);

            return new PlannedWalkRoute
            {
                Title = $"{effectiveDistanceKm:0.#} km {GetStyleTitle(walkStyle)} - {area.Name}",
                Summary = BuildRouteSummary(stops, effectiveDistanceKm, walkStyle, dogEnergy, usesCurrentLocation),
                TargetDistanceKm = effectiveDistanceKm,
                EstimatedDistanceKm = estimatedDistanceKm,
                EstimatedMinutes = Math.Max(10, (int)Math.Round(effectiveDistanceKm / GetSpeedKmPerHour(dogEnergy, walkStyle) * 60)),
                Stops = stops.OrderBy(stop => stop.Order).ToList(),
                RoutePoints = routePoints
            };
        }

        private static void AddNearestPlannerPlace(
            List<PlannedWalkRouteStop> stops,
            IReadOnlyList<PlannerPlace> places,
            string type,
            string label,
            string reason,
            ref int order)
        {
            var existingNames = stops.Select(stop => stop.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var place = places
                .Where(candidate => candidate.Type == type && !existingNames.Contains(candidate.Name))
                .OrderBy(candidate => candidate.Priority)
                .FirstOrDefault();

            if (place == null)
            {
                return;
            }

            stops.Add(new PlannedWalkRouteStop
            {
                Name = place.Name,
                Type = place.Type,
                Label = label,
                Reason = reason,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Order = order++
            });
        }

        private static IReadOnlyList<PlannedWalkPoint> BuildLoopPoints(
            double latitude,
            double longitude,
            double targetDistanceKm,
            IReadOnlyList<PlannedWalkRouteStop> stops)
        {
            var radiusKm = Math.Max(0.25, targetDistanceKm / (2 * Math.PI));
            var latDelta = radiusKm / 111.0;
            var lngDelta = radiusKm / (111.0 * Math.Cos(ToRadians(latitude)));
            var generated = new List<PlannedWalkPoint>
            {
                new() { Latitude = latitude, Longitude = longitude }
            };

            foreach (var stop in stops.Where(stop => stop.Type is not "start" and not "finish"))
            {
                generated.Add(new PlannedWalkPoint { Latitude = stop.Latitude, Longitude = stop.Longitude });
            }

            generated.AddRange([
                new PlannedWalkPoint { Latitude = latitude + latDelta, Longitude = longitude + lngDelta * 0.45 },
                new PlannedWalkPoint { Latitude = latitude + latDelta * 0.2, Longitude = longitude + lngDelta },
                new PlannedWalkPoint { Latitude = latitude - latDelta * 0.85, Longitude = longitude + lngDelta * 0.4 },
                new PlannedWalkPoint { Latitude = latitude - latDelta * 0.65, Longitude = longitude - lngDelta * 0.65 },
                new PlannedWalkPoint { Latitude = latitude + latDelta * 0.35, Longitude = longitude - lngDelta },
                new PlannedWalkPoint { Latitude = latitude, Longitude = longitude }
            ]);

            return generated;
        }

        private static string BuildRouteSummary(
            IReadOnlyList<PlannedWalkRouteStop> stops,
            double targetDistanceKm,
            string walkStyle,
            string dogEnergy,
            bool usesCurrentLocation)
        {
            var hasBin = stops.Any(stop => stop.Type == "bin");
            var hasPark = stops.Any(stop => stop.Type == "park");
            var hasShop = stops.Any(stop => stop.Type is "shop" or "cafe");
            var parts = new List<string>();

            if (hasBin)
            {
                parts.Add("vkljucen pasji kos");
            }

            if (hasPark)
            {
                parts.Add("park za daljsi postanek");
            }

            if (hasShop)
            {
                parts.Add("dog-friendly/pet shop postanek");
            }

            var intro = walkStyle switch
            {
                "quick" => "Hiter prakticen krog",
                "park" => "Bolj sproscen park sprehod",
                "city" => "Mestni socialni krog",
                "long" => "Daljsi raziskovalni sprehod",
                _ => "Predlog uravnotezenega kroga"
            };

            var energyNote = dogEnergy switch
            {
                "low" => "Tempo je nastavljen bolj umirjeno.",
                "high" => "Tempo je nastavljen bolj aktivno.",
                _ => "Tempo je srednje zivahen."
            };

            var locationNote = usesCurrentLocation
                ? " Izhodišče je tvoja trenutna lokacija."
                : string.Empty;

            return parts.Count == 0
                ? $"{intro} {targetDistanceKm:0.#} km. {energyNote}{locationNote}"
                : $"{intro} {targetDistanceKm:0.#} km: {string.Join(", ", parts)}. {energyNote}{locationNote}";
        }

        private static double EstimateRouteDistance(IReadOnlyList<PlannedWalkPoint> points)
        {
            if (points.Count < 2)
            {
                return 0;
            }

            var distance = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                distance += GetDistanceKm(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            }

            return distance;
        }

        private static IReadOnlyList<QuickWalkTemplate> BuildQuickWalkTemplates()
        {
            return
            [
                new QuickWalkTemplate
                {
                    Title = "Quick potty",
                    Subtitle = "Najhitrejsa pot do kosa in nazaj.",
                    WalkStyle = "quick",
                    DistanceKm = 1.5,
                    IncludeBins = true,
                    IncludePark = false,
                    IncludeWater = false,
                    IncludeDogFriendly = false
                },
                new QuickWalkTemplate
                {
                    Title = "Park loop",
                    Subtitle = "Vec vohanja in en dober park stop.",
                    WalkStyle = "park",
                    DistanceKm = 3.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = false
                },
                new QuickWalkTemplate
                {
                    Title = "City walk",
                    Subtitle = "Mestni krog z vodo in socialnim stopom.",
                    WalkStyle = "city",
                    DistanceKm = 4.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = true
                },
                new QuickWalkTemplate
                {
                    Title = "Trail mode",
                    Subtitle = "Daljsi aktivni sprehod z rezervnim kosom.",
                    WalkStyle = "long",
                    DistanceKm = 6.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = true
                }
            ];
        }

        private static string NormalizeDogEnergy(string? energy)
        {
            return (energy ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "low" => "low",
                "high" => "high",
                _ => "auto"
            };
        }

        private static double AdjustDistanceForEnergyAndStyle(double targetDistanceKm, string dogEnergy, string walkStyle)
        {
            var distance = targetDistanceKm;

            if (dogEnergy == "low")
            {
                distance -= 0.4;
            }
            else if (dogEnergy == "high")
            {
                distance += 0.8;
            }

            if (walkStyle == "quick")
            {
                distance = Math.Min(distance, 2.8);
            }
            else if (walkStyle == "long")
            {
                distance += 0.6;
            }

            return Math.Clamp(distance, 1, 12);
        }

        private static string GetStyleTitle(string walkStyle)
        {
            return walkStyle switch
            {
                "quick" => "quick walk",
                "park" => "park walk",
                "city" => "city walk",
                "long" => "long walk",
                _ => "balanced walk"
            };
        }

        private static double GetSpeedKmPerHour(string dogEnergy, string walkStyle)
        {
            var baseSpeed = dogEnergy switch
            {
                "low" => 3.8,
                "high" => 5.0,
                _ => 4.5
            };

            return walkStyle switch
            {
                "park" => baseSpeed - 0.4,
                "long" => baseSpeed + 0.2,
                _ => baseSpeed
            };
        }

        private static int GetBinReliabilityScore(TrashBin bin)
        {
            var score = 60;
            score += Math.Min(18, bin.UsefulVotes * 3);
            score += Math.Min(15, bin.UsedCount);
            score -= Math.Min(28, bin.FullReports * 8);
            score -= Math.Min(35, bin.MissingReports * 12);
            return Math.Clamp(score, 0, 100);
        }

        private static string GetBinReliabilityLabel(TrashBin bin)
        {
            var score = GetBinReliabilityScore(bin);
            if (score >= 80)
            {
                return "zelo dobra";
            }

            if (score >= 60)
            {
                return "dobra";
            }

            if (score >= 40)
            {
                return "srednja";
            }

            return "nizja";
        }

        private static bool IsValidPlannerCoordinate(double? latitude, double? longitude)
        {
            return latitude is >= -90 and <= 90
                && longitude is >= -180 and <= 180
                && Math.Abs(latitude.Value) > 0.0001
                && Math.Abs(longitude.Value) > 0.0001;
        }

        private static PlannerAreaCenter GetPlannerAreaCenter(string areaKey)
        {
            return areaKey switch
            {
                "ljubljana" => new PlannerAreaCenter("Ljubljana", 46.0569, 14.5058),
                "koper" => new PlannerAreaCenter("Koper / Obala", 45.5481, 13.7301),
                "celje" => new PlannerAreaCenter("Celje", 46.2397, 15.2677),
                "kranj" => new PlannerAreaCenter("Kranj", 46.2397, 14.3556),
                _ => new PlannerAreaCenter("Maribor", 46.5547, 15.6459)
            };
        }

        private static IReadOnlyList<PlannerPlace> GetPlannerPlaces(string areaKey)
        {
            return areaKey switch
            {
                "ljubljana" =>
                [
                    new PlannerPlace("Pasji park Tivoli", "park", 46.0567, 14.4965, 1),
                    new PlannerPlace("Pasji park Severni park", "park", 46.0615, 14.5210, 2),
                    new PlannerPlace("Pitnik Tivoli", "water", 46.0552, 14.4958, 1),
                    new PlannerPlace("Dog friendly center", "cafe", 46.0516, 14.5060, 1),
                    new PlannerPlace("Pasja trgovina Ljubljana", "shop", 46.0591, 14.5110, 1)
                ],
                "koper" =>
                [
                    new PlannerPlace("Pasji park Koper", "park", 45.5426, 13.7184, 1),
                    new PlannerPlace("Pasji park Izola", "park", 45.5365, 13.6619, 2),
                    new PlannerPlace("Voda Semedela", "water", 45.5438, 13.7195, 1),
                    new PlannerPlace("Dog friendly Obala", "cafe", 45.5464, 13.7242, 1),
                    new PlannerPlace("Pasja trgovina Koper", "shop", 45.5488, 13.7306, 1)
                ],
                "celje" =>
                [
                    new PlannerPlace("Pasji park Celje", "park", 46.2387, 15.2675, 1),
                    new PlannerPlace("Pasji park Lava", "park", 46.2289, 15.2518, 2),
                    new PlannerPlace("Voda ob Savinji", "water", 46.2375, 15.2662, 1),
                    new PlannerPlace("Dog friendly Celje", "cafe", 46.2394, 15.2668, 1),
                    new PlannerPlace("Pasja trgovina Celje", "shop", 46.2410, 15.2632, 1)
                ],
                "kranj" =>
                [
                    new PlannerPlace("Pasji park Kranj", "park", 46.2449, 14.3617, 1),
                    new PlannerPlace("Pasji park Strazisce", "park", 46.2508, 14.3325, 2),
                    new PlannerPlace("Voda Zlato polje", "water", 46.2458, 14.3598, 1),
                    new PlannerPlace("Dog friendly Kranj", "cafe", 46.2397, 14.3556, 1),
                    new PlannerPlace("Pasja trgovina Kranj", "shop", 46.2414, 14.3586, 1)
                ],
                _ =>
                [
                    new PlannerPlace("Pasji park Mestni park", "park", 46.5625, 15.6480, 1),
                    new PlannerPlace("Pasji park Tabor", "park", 46.5487, 15.6453, 2),
                    new PlannerPlace("Pitnik Lent", "water", 46.5572, 15.6467, 1),
                    new PlannerPlace("Dog friendly Lent", "cafe", 46.5578, 15.6452, 1),
                    new PlannerPlace("Pasja trgovina Maribor", "shop", 46.5540, 15.6484, 1)
                ]
            };
        }

        private static IReadOnlyList<AchievementItem> BuildWalkAchievements(int totalBinsAdded, int totalWalks, double totalDistanceKm, int uniqueParkVisits)
        {
            return
            [
                BuildAchievement("First walk", "Zakljuci prvi sprehod.", totalWalks, 1, "sprehodov"),
                BuildAchievement("10 km walked", "Skupaj prehodi 10 km.", totalDistanceKm, 10, "km"),
                BuildAchievement("100 km walked", "Skupaj prehodi 100 km.", totalDistanceKm, 100, "km"),
                BuildAchievement("Added 10 bins", "Dodaj 10 pasjih kosev.", totalBinsAdded, 10, "kosev"),
                BuildAchievement("Visited 5 parks", "Obisci 5 razlicnih pasjih parkov.", uniqueParkVisits, 5, "parkov")
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

        private static GamificationSummaryViewModel BuildGamificationSummary(
            IReadOnlyList<Walk> userCompletedWalks,
            IReadOnlyList<Walk> weeklyCompletedWalks,
            IReadOnlyList<TrashBin> recentContributorBins)
        {
            var walkedDates = userCompletedWalks
                .Select(walk => walk.StartedAt.Date)
                .Distinct()
                .OrderBy(date => date)
                .ToList();
            var currentStreak = CalculateCurrentStreak(walkedDates, DateTime.UtcNow.Date);
            var longestStreak = CalculateLongestStreak(walkedDates);
            var eightWeekStart = DateTime.UtcNow.Date.AddDays(-55);
            var activeWeeks = userCompletedWalks
                .Where(walk => walk.StartedAt.Date >= eightWeekStart)
                .Select(walk => GetWeekKey(walk.StartedAt.Date))
                .Distinct()
                .Count();

            return new GamificationSummaryViewModel
            {
                CurrentDailyStreak = currentStreak,
                LongestDailyStreak = longestStreak,
                ActiveWeeksLastEight = activeWeeks,
                WeeklyDistanceLeaders = weeklyCompletedWalks
                    .GroupBy(walk => GetLeaderboardDisplayName(walk.Owner))
                    .Select(group => new LeaderboardEntry
                    {
                        Name = group.Key,
                        Subtitle = $"{group.Count()} sprehodov ta teden",
                        ValueText = $"{group.Sum(walk => walk.DistanceMeters) / 1000:0.0} km"
                    })
                    .OrderByDescending(item => ParseLeadingNumber(item.ValueText))
                    .Take(5)
                    .ToList(),
                MostActiveDogs = weeklyCompletedWalks
                    .Where(walk => walk.Dog != null)
                    .GroupBy(walk => new { walk.Dog!.Name, OwnerName = GetLeaderboardDisplayName(walk.Owner) })
                    .Select(group => new LeaderboardEntry
                    {
                        Name = group.Key.Name,
                        Subtitle = group.Key.OwnerName,
                        ValueText = $"{group.Count()} sprehodov"
                    })
                    .OrderByDescending(item => ParseLeadingNumber(item.ValueText))
                    .Take(5)
                    .ToList(),
                TopContributors = recentContributorBins
                    .GroupBy(bin => GetLeaderboardDisplayName(bin.User))
                    .Select(group => new LeaderboardEntry
                    {
                        Name = group.Key,
                        Subtitle = "Dodani pasji kosi v zadnjih 28 dneh",
                        ValueText = $"{group.Count()} dodanih"
                    })
                    .OrderByDescending(item => ParseLeadingNumber(item.ValueText))
                    .Take(5)
                    .ToList()
            };
        }

        private static string GetLeaderboardDisplayName(ApplicationUser? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? "DoggyDrop uporabnik";
        }

        private static int CalculateCurrentStreak(IReadOnlyList<DateTime> walkedDates, DateTime today)
        {
            if (walkedDates.Count == 0)
            {
                return 0;
            }

            var dateSet = walkedDates.ToHashSet();
            var cursor = dateSet.Contains(today) ? today : today.AddDays(-1);
            var streak = 0;

            while (dateSet.Contains(cursor))
            {
                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        private static int CalculateLongestStreak(IReadOnlyList<DateTime> walkedDates)
        {
            var longest = 0;
            var current = 0;
            DateTime? previous = null;

            foreach (var date in walkedDates)
            {
                current = previous.HasValue && date == previous.Value.AddDays(1)
                    ? current + 1
                    : 1;
                longest = Math.Max(longest, current);
                previous = date;
            }

            return longest;
        }

        private static string GetWeekKey(DateTime date)
        {
            var calendar = CultureInfo.InvariantCulture.Calendar;
            var week = calendar.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            return $"{date.Year}-{week}";
        }

        private static double ParseLeadingNumber(string text)
        {
            var numeric = new string(text.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
            return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        private sealed record PlannerAreaCenter(string Name, double Latitude, double Longitude);

        private sealed record PlannerPlace(string Name, string Type, double Latitude, double Longitude, int Priority);
    }

    public class WalkPointInput
    {
        public double Latitude { get; set; }

        public double Longitude { get; set; }

        public DateTime? RecordedAt { get; set; }
    }

    public class ToggleStopInput
    {
        public bool Completed { get; set; }
    }
}
