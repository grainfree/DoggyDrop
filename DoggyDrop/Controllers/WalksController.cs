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
using System.Text.Json;

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
        private readonly IGamificationRewardBuilder _rewardBuilder;
        private readonly IGamificationCalendar _gamificationCalendar;
        private readonly IUserAchievementService _userAchievementService;

        public WalksController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            ICloudinaryService cloudinaryService,
            IGamificationService gamificationService,
            IDogProgressionService dogProgressionService,
            IOsmWalkPlannerService osmWalkPlannerService,
            IGamificationRewardBuilder rewardBuilder,
            IGamificationCalendar gamificationCalendar,
            IUserAchievementService userAchievementService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _cloudinaryService = cloudinaryService;
            _gamificationService = gamificationService;
            _dogProgressionService = dogProgressionService;
            _osmWalkPlannerService = osmWalkPlannerService;
            _rewardBuilder = rewardBuilder;
            _gamificationCalendar = gamificationCalendar;
            _userAchievementService = userAchievementService;
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
                .Include(w => w.Points)
                .Include(w => w.PlannedWalk)
                .Include(w => w.StopCompletions!)
                .Include(w => w.Photos!)
                .FirstOrDefaultAsync(w => w.OwnerId == userId && w.Status == "Active");

            if (activeWalk != null && await RecoverStaleWalkAsync(activeWalk))
            {
                activeWalk = null;
                TempData["ErrorMessage"] = "Prejšnji sprehod je bil zaradi daljše prekinitve GPS sledenja zaprt pri zadnji zabeleženi točki. Nagrad ni podelil.";
            }

            var interruptedWalks = await _context.Walks
                .AsNoTracking()
                .Include(walk => walk.Dog)
                .Where(walk => walk.OwnerId == userId && walk.Status == "Interrupted")
                .OrderByDescending(walk => walk.EndedAt)
                .Take(5)
                .ToListAsync();

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
            var ownedAchievements = await _userAchievementService.GetOwnedAsync(userId!);
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
            var canonicalWalkStreak = await _gamificationService.GetStreakAsync(userId!, GamificationStreakConstants.Walk);
            var activityInsights = ActivityInsightsBuilder.Build(completedWalks);
            activityInsights.CurrentStreakDays = canonicalWalkStreak.EffectiveCurrentDays;
            activityInsights.LongestStreakDays = canonicalWalkStreak.LongestDays;

            var model = new WalksIndexViewModel
            {
                Dogs = dogs,
                ActiveWalk = activeWalk,
                InterruptedWalks = interruptedWalks,
                RecentWalks = recentWalks,
                TotalDistanceKm = totalDistanceKm,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                CompletedWalkCount = completedWalks.Count,
                SelectedDogId = dogId,
                AverageDistanceKm = completedWalks.Count == 0 ? 0 : totalDistanceKm / completedWalks.Count,
                LongestWalkKm = completedWalks.Count == 0 ? 0 : completedWalks.Max(w => w.DistanceMeters) / 1000,
                UsedBinsCount = completedWalks.Sum(w => w.UsedBinsCount),
                WeeklyStats = BuildWeeklyStats(completedWalks),
                ActivityInsights = activityInsights,
                WalkStreak = new GamificationStreakViewModel
                {
                    StreakType = canonicalWalkStreak.StreakType,
                    Label = canonicalWalkStreak.Label,
                    StoredCurrentDays = canonicalWalkStreak.StoredCurrentDays,
                    CurrentDays = canonicalWalkStreak.EffectiveCurrentDays,
                    LongestDays = canonicalWalkStreak.LongestDays,
                    FlameTier = canonicalWalkStreak.FlameTier,
                    State = canonicalWalkStreak.State,
                    IsSafeToday = canonicalWalkStreak.IsSafeToday,
                    IsAtRiskToday = canonicalWalkStreak.IsAtRiskToday,
                    Guidance = canonicalWalkStreak.Guidance
                },
                RecentPlans = recentPlans,
                Achievements = UserAchievementPresentationBuilder.Build(
                    new UserAchievementProgress(dogs.Count, completedWalks.Count, totalDistanceKm, totalBinsAdded, uniqueParkVisits),
                    ownedAchievements,
                    "walks"),
                Gamification = BuildGamificationSummary(completedWalks, weeklyCompletedWalks, recentContributorBins, canonicalWalkStreak),
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
            var selectedDogEnergy = "auto";

            var bins = await _context.TrashBins
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var route = await BuildPlannerRouteAsync(
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
                includeDogFriendly,
                preferExternalRouting: false);

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
                Presets = BuildQuickWalkTemplates(),
                WalkStyle = selectedWalkStyle,
                DogEnergy = selectedDogEnergy,
                Route = route
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

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockWalkStartUserAsync(userId);
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
                var startStop = await _context.PlannedWalkStops
                    .FirstOrDefaultAsync(stop => stop.PlannedWalkId == plannedWalk.Id && stop.Type == "start");
                if (startStop != null)
                {
                    walk.StopCompletions = new List<WalkStopCompletion>
                    {
                        new() { PlannedWalkStop = startStop, UserId = userId, CompletedAt = walk.StartedAt }
                    };
                }
            }

            if (plannedWalk != null)
            {
                plannedWalk.UsedAt = DateTime.UtcNow;
            }

            _context.Walks.Add(walk);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            await NotifyFriendsAboutWalkStartAsync(walk.Id, userId, dogId, plannedWalk?.Title);

            return RedirectToAction(nameof(Active), new { id = walk.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartPlanned(
            int dogId,
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

            var dogExists = await _context.Dogs.AnyAsync(dog => dog.Id == dogId && dog.OwnerId == userId);
            if (!dogExists)
            {
                return NotFound();
            }

            var activeWalk = await _context.Walks
                .FirstOrDefaultAsync(walk => walk.OwnerId == userId && walk.Status == "Active");
            if (activeWalk != null)
            {
                TempData["ErrorMessage"] = "Najprej zaključi trenutni sprehod.";
                return RedirectToAction(nameof(Active), new { id = activeWalk.Id });
            }

            var areaKey = GetPlannerAreas().Any(candidate => candidate.Key == area) ? area : "maribor";
            var hasCurrentLocation = IsValidPlannerCoordinate(latitude, longitude);
            var start = hasCurrentLocation
                ? new PlannerAreaCenter("Moja lokacija", latitude!.Value, longitude!.Value)
                : GetPlannerAreaCenter(areaKey);
            var safeDistanceKm = Math.Clamp(distanceKm, 1, 12);
            var selectedWalkStyle = GetPlannerStyles().Any(item => item.Key == walkStyle) ? walkStyle! : "balanced";
            var selectedDogEnergy = "auto";
            var bins = await _context.TrashBins
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var route = await BuildPlannerRouteAsync(areaKey, start, hasCurrentLocation, safeDistanceKm, bins, selectedWalkStyle, selectedDogEnergy, includeBins, includePark, includeWater, includeDogFriendly, preferExternalRouting: true);

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
                UsedAt = DateTime.UtcNow,
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

            var walk = new Walk
            {
                DogId = dogId,
                OwnerId = userId,
                StartedAt = DateTime.UtcNow,
                Status = "Active",
                PlannedWalk = plan
            };

            var plannedStart = plan.Stops.FirstOrDefault(stop => stop.Type == "start");
            if (plannedStart != null)
            {
                walk.StopCompletions = new List<WalkStopCompletion>
                {
                    new() { PlannedWalkStop = plannedStart, UserId = userId, CompletedAt = walk.StartedAt }
                };
            }

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                await LockWalkStartUserAsync(userId);
                var currentActiveWalkId = await _context.Walks
                    .Where(candidate => candidate.OwnerId == userId && candidate.Status == "Active")
                    .Select(candidate => (int?)candidate.Id)
                    .FirstOrDefaultAsync();
                if (currentActiveWalkId.HasValue)
                {
                    TempData["ErrorMessage"] = "Najprej zaključi trenutni sprehod.";
                    return RedirectToAction(nameof(Active), new { id = currentActiveWalkId.Value });
                }

                _context.Walks.Add(walk);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            await _gamificationService.AwardXpAsync(
                userId,
                GamificationConstants.CreateRoute,
                GamificationConstants.CreateRouteXp,
                nameof(PlannedWalk),
                plan.Id.ToString(),
                "Nova pot");
            await NotifyFriendsAboutWalkStartAsync(walk.Id, userId, dogId, plan.Title);

            TempData["SuccessMessage"] = "Sprehod je zagnan s planirano potjo.";
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
            var selectedDogEnergy = "auto";
            var bins = await _context.TrashBins
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var route = await BuildPlannerRouteAsync(areaKey, start, hasCurrentLocation, safeDistanceKm, bins, selectedWalkStyle, selectedDogEnergy, includeBins, includePark, includeWater, includeDogFriendly, preferExternalRouting: true);

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

            if (walk.Status == "Active" && await RecoverStaleWalkAsync(walk))
            {
                TempData["ErrorMessage"] = "Sprehod je bil prekinjen po več kot 24 urah brez GPS točke. Ohranili smo že zabeleženo pot in razdaljo; novih nagrad ni.";
                return RedirectToAction(nameof(Interrupted), new { id });
            }

            if (walk.Status == "Interrupted") return RedirectToAction(nameof(Interrupted), new { id });
            if (walk.Status == "Completed") return RedirectToAction(nameof(Details), new { id });

            return View(walk);
        }

        [HttpGet]
        public async Task<IActionResult> Interrupted(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks.AsNoTracking()
                .Include(item => item.Dog)
                .Include(item => item.Points)
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId && item.Status == "Interrupted");
            return walk == null ? NotFound() : View(walk);
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
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.RoutePoints)
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
                .AsSplitQuery()
                .FirstOrDefaultAsync(w => w.Id == id && (w.OwnerId == userId || w.Status == "Completed"));

            if (walk == null)
            {
                return NotFound();
            }

            if (walk.Status == "Active")
            {
                return RedirectToAction(nameof(Active), new { id = walk.Id });
            }

            if (walk.Status == "Interrupted")
            {
                return RedirectToAction(nameof(Interrupted), new { id = walk.Id });
            }

            if (walk.Status != "Completed")
            {
                return NotFound();
            }

            if (walk.OwnerId == userId && TempData.TryGetValue(GetWalkRewardTempDataKey(walk.Id), out var rewardResultJson))
            {
                try
                {
                    var rewardResult = JsonSerializer.Deserialize<GamificationRewardResultViewModel>(rewardResultJson?.ToString() ?? string.Empty);
                    ViewBag.WalkRewardResult = rewardResult?.WalkId == walk.Id ? rewardResult : null;
                }
                catch (JsonException)
                {
                    ViewBag.WalkRewardResult = null;
                }
            }

            var walkReference = walk.Id.ToString(CultureInfo.InvariantCulture);
            var userXpEvents = walk.OwnerId == userId
                ? await _context.UserXpEvents.AsNoTracking()
                    .Where(item => item.UserId == userId && item.ReferenceType == nameof(Walk) && item.ReferenceId == walkReference)
                    .ToListAsync()
                : [];
            var dogXpEvents = walk.OwnerId == userId
                ? await _context.DogXpEvents.AsNoTracking()
                    .Where(item => item.DogId == walk.DogId && item.ReferenceType == nameof(Walk) && item.ReferenceId == walkReference)
                    .ToListAsync()
                : [];
            var achievements = walk.OwnerId == userId
                ? await _context.UserAchievements.AsNoTracking()
                    .Where(item => item.UserId == userId && item.SourceType == nameof(Walk) && item.SourceId == walkReference)
                    .ToListAsync()
                : [];
            var memory = WalkMemoryPresentation.Build(walk, userXpEvents, dogXpEvents, achievements, walk.OwnerId == userId);
            ViewBag.WalkMemory = memory;
            return View(walk);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPhoto(int id, IFormFile? photo, string? caption, int? plannedWalkStopId = null)
        {
            var wantsJson = Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);
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
                if (wantsJson) return BadRequest(new { error = "Fotografija ni bila izbrana." });
                TempData["ErrorMessage"] = "Fotografija ni bila izbrana.";
                return RedirectToPhotoSource(walk);
            }

            var imageUrl = await _cloudinaryService.UploadWalkImageAsync(photo);
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                if (wantsJson) return BadRequest(new { error = "Fotografije ni bilo mogoče shraniti." });
                TempData["ErrorMessage"] = "Fotografije ni bilo mogoce shraniti.";
                return RedirectToPhotoSource(walk);
            }

            if (plannedWalkStopId.HasValue)
            {
                var hasStop = walk.PlannedWalk?.Stops?.Any(stop => stop.Id == plannedWalkStopId.Value) ?? false;
                if (!hasStop)
                {
                    if (wantsJson) return BadRequest(new { error = "Izbran postanek za fotografijo ni veljaven." });
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

            if (wantsJson) return Ok(new { message = "Fotografija sprehoda je dodana." });
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
            if (string.IsNullOrEmpty(userId)) return Challenge();
            if (input == null || !double.IsFinite(input.Latitude) || !double.IsFinite(input.Longitude) ||
                input.Latitude is < -90 or > 90 || input.Longitude is < -180 or > 180 ||
                (input.AccuracyMeters.HasValue && (!double.IsFinite(input.AccuracyMeters.Value) || input.AccuracyMeters.Value < 0 || input.AccuracyMeters.Value > 1000)))
                return BadRequest(new { outcome = "invalid" });

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockWalkAsync(id, userId);
            var walk = await _context.Walks
                .FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);

            if (walk == null)
            {
                return NotFound();
            }
            if (walk.Status != "Active") return Conflict(new { outcome = "no-longer-active", status = walk.Status });

            if (await RecoverStaleWalkLockedAsync(walk))
            {
                await transaction.CommitAsync();
                return Conflict(new { outcome = "no-longer-active", status = "Interrupted" });
            }

            var nowUtc = DateTime.UtcNow;
            if (input.RecordedAt is { } suppliedAt &&
                (suppliedAt.Kind != DateTimeKind.Utc || suppliedAt < nowUtc.AddMinutes(-2) || suppliedAt > nowUtc.AddMinutes(1)))
                return BadRequest(new { outcome = "invalid" });
            var recordedAt = input.RecordedAt is { } reportedAt &&
                reportedAt.Kind == DateTimeKind.Utc
                    ? reportedAt
                    : nowUtc;
            var lastPoint = await _context.WalkPoints.AsNoTracking()
                .Where(point => point.WalkId == id)
                .OrderByDescending(point => point.RecordedAt).ThenByDescending(point => point.Id)
                .FirstOrDefaultAsync();

            async Task<IActionResult> RejectedPoint(string outcome)
            {
                var count = await _context.WalkPoints.CountAsync(point => point.WalkId == id);
                await transaction.CommitAsync();
                return Json(new { outcome, walk.DistanceMeters, pointCount = count });
            }

            if (lastPoint != null)
            {
                var separation = GetDistanceMeters(lastPoint.Latitude, lastPoint.Longitude, input.Latitude, input.Longitude);
                if (recordedAt <= lastPoint.RecordedAt)
                    return await RejectedPoint(recordedAt == lastPoint.RecordedAt && separation < 2 ? "duplicate" : "out-of-order");
                if (separation < 2 && recordedAt - lastPoint.RecordedAt < TimeSpan.FromSeconds(2))
                    return await RejectedPoint("duplicate");
                // Only reject implausible teleports; ordinary noisy walking samples remain accepted.
                var elapsedSeconds = (recordedAt - lastPoint.RecordedAt).TotalSeconds;
                if (separation > Math.Max(150, elapsedSeconds * 30 + 2 * (input.AccuracyMeters ?? 0)))
                    return BadRequest(new { outcome = "invalid" });
                walk.DistanceMeters += separation;
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
            var pointCount = await _context.WalkPoints.CountAsync(candidate => candidate.WalkId == walk.Id);
            await transaction.CommitAsync();

            return Json(new
            {
                outcome = "accepted",
                walk.DistanceMeters,
                pointCount
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
            IActionResult FinishRedirect(string action)
            {
                var redirect = RedirectToAction(action, new { id });
                return Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase)
                    ? Json(new { redirectUrl = Url.Action(action, "Walks", new { id }) })
                    : redirect;
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockWalkAsync(id, userId);
            var walk = await _context.Walks
                .AsNoTracking()
                .Include(w => w.Dog)
                .Include(w => w.Points)
                .FirstOrDefaultAsync(w => w.Id == id && w.OwnerId == userId);

            if (walk == null)
            {
                return NotFound();
            }

            if (walk.Status != "Active")
            {
                if (walk.Status == "Interrupted")
                {
                    TempData["ErrorMessage"] = "Ta sprehod je bil prekinjen po dolgem premoru. Ohranili smo GPS pot in razdaljo, brez novih nagrad.";
                    return FinishRedirect(nameof(Interrupted));
                }
                return FinishRedirect(nameof(Details));
            }

            if (await RecoverStaleWalkLockedAsync(walk))
            {
                await transaction.CommitAsync();
                TempData["ErrorMessage"] = "Sprehod je bil prekinjen pri zadnji GPS točki; zaključek po dolgem premoru ne podeli nagrad.";
                return FinishRedirect(nameof(Interrupted));
            }

            var distanceMeters = TryParseDistanceKm(manualDistanceKm, out var parsedDistanceKm)
                ? parsedDistanceKm * 1000
                : walk.DistanceMeters;
            var binsUsed = usedBinsCount.HasValue
                ? Math.Clamp(usedBinsCount.Value, 0, 50)
                : walk.UsedBinsCount;
            var endedAt = _gamificationCalendar.UtcNow.UtcDateTime;

            var completedRows = await _context.Walks
                .Where(candidate => candidate.Id == id && candidate.OwnerId == userId && candidate.Status == "Active")
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.DistanceMeters, distanceMeters)
                    .SetProperty(candidate => candidate.UsedBinsCount, binsUsed)
                    .SetProperty(candidate => candidate.EndedAt, endedAt)
                    .SetProperty(candidate => candidate.Status, "Completed"));

            if (completedRows == 0)
            {
                await transaction.RollbackAsync();
                return FinishRedirect(nameof(Details));
            }

            walk = await _context.Walks
                .AsNoTracking()
                .Include(candidate => candidate.Dog)
                .SingleAsync(candidate => candidate.Id == id);

            var userProfile = await _gamificationService.EnsureProfileAsync(userId!);
            var previousUserLevel = _gamificationService.CalculateLevelInfo(userProfile.TotalXp);
            var dogProfile = await _dogProgressionService.EnsureProfileAsync(walk.DogId);
            var previousDogLevel = _dogProgressionService.CalculateLevelInfo(dogProfile.TotalXp);
            var previousDogProfile = _rewardBuilder.Snapshot(dogProfile);
            var previousWalkStreak = await _context.UserStreaks.AsNoTracking()
                .FirstOrDefaultAsync(streak => streak.UserId == userId && streak.StreakType == GamificationStreakConstants.Walk);
            var previousWalkStreakDays = _gamificationService
                .GetEffectiveStreak(previousWalkStreak, GamificationStreakConstants.Walk)
                .EffectiveCurrentDays;
            var previousCompletedWalkCount = await _context.Walks
                .CountAsync(candidate => candidate.OwnerId == userId && candidate.Status == "Completed" && candidate.Id != id);
            var previousTotalDistanceKm = await _context.Walks
                .Where(candidate => candidate.OwnerId == userId && candidate.Status == "Completed" && candidate.Id != id)
                .SumAsync(candidate => candidate.DistanceMeters) / 1000d;
            var isFirstCompletedWalk = previousCompletedWalkCount == 0;

            var distanceXp = (int)Math.Floor(walk.DistanceMeters / 1000d) * GamificationConstants.WalkDistanceXpPerKm;
            var userXpEvent = await _gamificationService.AwardXpAsync(
                userId,
                GamificationConstants.WalkDistance,
                distanceXp,
                nameof(Walk),
                walk.Id.ToString(),
                "Zakljucen sprehod");
            var walkStreak = await _gamificationService.RecordStreakActivityAtAsync(userId, GamificationStreakConstants.Walk, endedAt);
            var dogXpEvent = await _dogProgressionService.AwardXpAsync(
                walk.DogId,
                "CompletedWalk",
                Math.Max(10, (int)Math.Round(walk.DistanceMeters / 1000d * 18)),
                BuildDogWalkStats(walk),
                nameof(Walk),
                walk.Id.ToString(),
                "Zakljucen sprehod");

            var currentCompletedWalkCount = previousCompletedWalkCount + 1;
            var currentTotalDistanceKm = previousTotalDistanceKm + walk.DistanceMeters / 1000d;
            var achievementUnlocks = new List<AchievementUnlockResult>
            {
                await _userAchievementService.TryUnlockAsync(userId!, UserAchievementCatalog.WalkFirst, endedAt, nameof(Walk), walk.Id.ToString())
            };
            if (currentTotalDistanceKm >= 10)
                achievementUnlocks.Add(await _userAchievementService.TryUnlockAsync(userId!, UserAchievementCatalog.Walk10Km, endedAt, nameof(Walk), walk.Id.ToString()));
            if (currentTotalDistanceKm >= 100)
                achievementUnlocks.Add(await _userAchievementService.TryUnlockAsync(userId!, UserAchievementCatalog.Walk100Km, endedAt, nameof(Walk), walk.Id.ToString()));

            var rewardResult = await BuildWalkRewardResultAsync(
                walk,
                previousUserLevel,
                previousDogLevel,
                previousDogProfile,
                previousWalkStreakDays,
                previousCompletedWalkCount,
                previousTotalDistanceKm,
                achievementUnlocks,
                userXpEvent,
                dogXpEvent,
                walkStreak);

            await transaction.CommitAsync();

            if (rewardResult.HasRewards)
            {
                TempData[GetWalkRewardTempDataKey(walk.Id)] = JsonSerializer.Serialize(rewardResult);
            }

            TempData["SuccessMessage"] = "Sprehod je shranjen.";
            if (isFirstCompletedWalk)
            {
                TempData[GetFirstWalkTempDataKey("Show", walk.Id)] = true;
                TempData[GetFirstWalkTempDataKey("DogName", walk.Id)] = walk.DogId.ToString();
                TempData[GetFirstWalkTempDataKey("DistanceKm", walk.Id)] = (walk.DistanceMeters / 1000d).ToString("0.00", CultureInfo.InvariantCulture);
            }

            return FinishRedirect(nameof(Details));
        }

        private static string GetWalkRewardTempDataKey(int walkId) => $"WalkRewardResult:{walkId}";

        private async Task LockWalkStartUserAsync(string userId)
        {
            if (_context.Database.IsNpgsql())
            {
                await _context.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
                    .AsNoTracking().ToListAsync();
            }
            else if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite has no row locks; take its writer lock before checking active walks.
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"Id\" = \"Id\" WHERE \"Id\" = {userId}");
            }
            else throw new NotSupportedException("Walk creation requires PostgreSQL or SQLite transaction locking.");
        }

        private async Task LockWalkAsync(int id, string ownerId)
        {
            if (_context.Database.IsNpgsql())
            {
                await _context.Walks.FromSqlInterpolated($"SELECT * FROM \"Walks\" WHERE \"Id\" = {id} AND \"OwnerId\" = {ownerId} FOR UPDATE")
                    .AsNoTracking().ToListAsync();
            }
            else if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite has no row locks. Acquire its writer lock before reading the walk.
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Walks\" SET \"Status\" = \"Status\" WHERE \"Id\" = {id} AND \"OwnerId\" = {ownerId}");
            }
            else throw new NotSupportedException("Walk recording requires PostgreSQL or SQLite transaction locking.");
        }

        private async Task<bool> RecoverStaleWalkAsync(Walk walk)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockWalkAsync(walk.Id, walk.OwnerId);
            var current = await _context.Walks.AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == walk.Id && candidate.OwnerId == walk.OwnerId);
            if (current == null || current.Status != "Active") return false;
            var recovered = await RecoverStaleWalkLockedAsync(current);
            await transaction.CommitAsync();
            return recovered;
        }

        private async Task<bool> RecoverStaleWalkLockedAsync(Walk walk)
        {
            var nowUtc = _gamificationCalendar.UtcNow.UtcDateTime;
            var points = await _context.WalkPoints
                .AsNoTracking()
                .Where(point => point.WalkId == walk.Id)
                .ToListAsync();
            if (!WalkStaleness.IsStale(walk, points, nowUtc)) return false;

            var lastActivity = WalkStaleness.LastActivity(walk, points, nowUtc);
            return await _context.Walks
                .Where(candidate => candidate.Id == walk.Id && candidate.OwnerId == walk.OwnerId && candidate.Status == "Active")
                .ExecuteUpdateAsync(updates => updates
                    .SetProperty(candidate => candidate.EndedAt, lastActivity)
                    .SetProperty(candidate => candidate.Status, "Interrupted")) > 0;
        }

        private static string GetFirstWalkTempDataKey(string valueName, int walkId) => $"FirstWalk:{valueName}:{walkId}";

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

        private static string BuildShareCardSvg(Walk walk)
        {
            var dogName = EscapeSvg(walk.Dog?.Name ?? "Pes");
            var dogNameSizing = dogName.Length > 12 ? "textLength=\"900\" lengthAdjust=\"spacingAndGlyphs\"" : string.Empty;
            var distanceKm = (walk.DistanceMeters / 1000).ToString("0.00", CultureInfo.GetCultureInfo("sl-SI"));
            var duration = walk.EndedAt.HasValue
                ? (walk.EndedAt.Value - walk.StartedAt).ToString(@"hh\:mm")
                : "00:00";
            var bins = walk.UsedBinsCount.ToString(CultureInfo.InvariantCulture);
            var dateText = walk.StartedAt.ToLocalTime().ToString("dd.MM.yyyy");
            var photoUrls = (walk.Photos ?? [])
                .OrderByDescending(photo => photo.CreatedAt)
                .Select(photo => photo.ImageUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Take(1)
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
    <linearGradient id="shade" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0%" stop-color="#102b24" stop-opacity="0.5"/>
      <stop offset="35%" stop-color="#102b24" stop-opacity="0"/>
      <stop offset="100%" stop-color="#102b24" stop-opacity="0.95"/>
    </linearGradient>
  </defs>
  <rect width="1080" height="1920" fill="#24695a"/>
  {{photoLayout}}
  <rect width="1080" height="1920" fill="url(#shade)"/>
  <text x="80" y="130" fill="#ffffff" font-size="42" font-family="Arial, sans-serif" font-weight="800">DOGGYDROP</text>
  <text x="80" y="1440" fill="#ffffff" font-size="96" font-family="Arial, sans-serif" font-weight="800" {{dogNameSizing}}>{{dogName}}</text>
  <text x="80" y="1600" fill="#ffffff" font-size="152" font-family="Arial, sans-serif" font-weight="900">{{distanceKm}} km</text>
  <text x="80" y="1700" fill="#ffffff" font-size="44" font-family="Arial, sans-serif" font-weight="700">{{duration}} · {{bins}} košev</text>
  <text x="80" y="1780" fill="#ffffff" font-size="40" font-family="Arial, sans-serif">{{EscapeSvg(dateText)}}</text>
  <text x="80" y="1860" fill="#ffffff" font-size="36" font-family="Arial, sans-serif">doggydrop.app</text>
</svg>
""";
        }

        private static string BuildSharePhotoLayout(IReadOnlyList<string> safePhotoUrls)
        {
            if (safePhotoUrls.Count == 0)
            {
                return string.Empty;
            }

            return $"<image href=\"{safePhotoUrls[0]}\" width=\"1080\" height=\"1920\" preserveAspectRatio=\"xMidYMid slice\"/>";
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

        private async Task<PlannedWalkRoute> BuildPlannerRouteAsync(
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
            bool includeDogFriendly,
            bool preferExternalRouting)
        {
            if (usesCurrentLocation && preferExternalRouting)
            {
                var osmRoute = await _osmWalkPlannerService.PlanAsync(
                    area.Latitude,
                    area.Longitude,
                    targetDistanceKm,
                    bins,
                    walkStyle,
                    dogEnergy,
                    includeBins,
                    includePark,
                    includeWater,
                    includeDogFriendly,
                    HttpContext.RequestAborted);

                if (osmRoute != null)
                {
                    return osmRoute;
                }
            }

            return BuildPlannedRoute(
                areaKey,
                area,
                usesCurrentLocation,
                targetDistanceKm,
                bins,
                walkStyle,
                dogEnergy,
                includeBins,
                includePark,
                includeWater,
                includeDogFriendly);
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
                    Reason = "Začetna točka kroga.",
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
                        Label = "Pasji koš",
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
                Reason = "Zaključek krožne poti.",
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
                    Title = "Hitro lulanje",
                    Subtitle = "Kratek krog do najbližjega koša in nazaj.",
                    WalkStyle = "quick",
                    DistanceKm = 1.5,
                    IncludeBins = true,
                    IncludePark = false,
                    IncludeWater = false,
                    IncludeDogFriendly = false
                },
                new QuickWalkTemplate
                {
                    Title = "Klasičen krog",
                    Subtitle = "Uravnotežen sprehod s košem, zeleno točko in malo ritma.",
                    WalkStyle = "balanced",
                    DistanceKm = 3.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = false
                },
                new QuickWalkTemplate
                {
                    Title = "Park & vohanje",
                    Subtitle = "Počasnejši tempo, več smrčkanja in park, če je blizu.",
                    WalkStyle = "park",
                    DistanceKm = 3.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = false
                },
                new QuickWalkTemplate
                {
                    Title = "Mestna šapa",
                    Subtitle = "Krog z vodo in dog-friendly postankom, ko se gre v urbano.",
                    WalkStyle = "city",
                    DistanceKm = 4.5,
                    IncludeBins = true,
                    IncludePark = true,
                    IncludeWater = true,
                    IncludeDogFriendly = true
                },
                new QuickWalkTemplate
                {
                    Title = "Raziskovanje",
                    Subtitle = "Daljša pot z rezervnim košem in več odkrivanja.",
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

        private async Task<GamificationRewardResultViewModel> BuildWalkRewardResultAsync(
            Walk walk,
            GamificationLevelInfo previousUserLevel,
            DogProgressionLevelInfo previousDogLevel,
            DogProgressionProfile previousDogProfile,
            int previousWalkStreakDays,
            int previousCompletedWalkCount,
            double previousTotalDistanceKm,
            IReadOnlyList<AchievementUnlockResult> achievementUnlocks,
            UserXpEvent? userXpEvent,
            DogXpEvent? dogXpEvent,
            UserStreak? walkStreak)
        {
            var currentUserLevel = await _gamificationService.GetLevelInfoAsync(walk.OwnerId);
            var currentDogProfile = await _context.DogProgressionProfiles
                .AsNoTracking()
                .FirstAsync(profile => profile.DogId == walk.DogId);
            var currentDogLevel = _dogProgressionService.CalculateLevelInfo(currentDogProfile.TotalXp);
            var currentTotalDistanceKm = previousTotalDistanceKm + walk.DistanceMeters / 1000d;
            var ownedKeys = (await _userAchievementService.GetOwnedAsync(walk.OwnerId))
                .Select(item => item.AchievementKey).ToHashSet(StringComparer.Ordinal);
            var unlockedAchievements = achievementUnlocks.Where(item => item.NewlyUnlocked)
                .Select(item =>
                {
                    var definition = UserAchievementCatalog.Get(item.AchievementKey);
                    return new RewardAchievementViewModel { Key = definition.Key, Name = definition.DisplayName, Description = definition.Description };
                }).ToList();

            return new GamificationRewardResultViewModel
            {
                WalkId = walk.Id,
                UserReward = _rewardBuilder.BuildUserReward(userXpEvent, previousUserLevel, currentUserLevel),
                DogReward = _rewardBuilder.BuildDogReward(walk.Dog ?? new Dog { Id = walk.DogId, Name = "Pes" }, dogXpEvent, previousDogLevel, currentDogLevel, previousDogProfile, currentDogProfile),
                StreakReward = _rewardBuilder.BuildStreakReward(
                    _gamificationService.GetEffectiveStreak(walkStreak, GamificationStreakConstants.Walk),
                    previousWalkStreakDays),
                UnlockedAchievements = unlockedAchievements,
                NextGoal = BuildWalkNextGoal(ownedKeys, currentTotalDistanceKm, currentUserLevel, walk.DogId)
            };
        }

        private RewardNextGoalViewModel BuildWalkNextGoal(
            IReadOnlySet<string> ownedKeys,
            double totalDistanceKm,
            GamificationLevelInfo currentUserLevel,
            int dogId)
        {
            var nextKey = !ownedKeys.Contains(UserAchievementCatalog.Walk10Km)
                ? UserAchievementCatalog.Walk10Km
                : !ownedKeys.Contains(UserAchievementCatalog.Walk100Km) ? UserAchievementCatalog.Walk100Km : null;
            if (nextKey != null)
            {
                var definition = UserAchievementCatalog.Get(nextKey);
                var targetKm = definition.ProgressTarget;
                var remainingKm = Math.Max(0, targetKm - totalDistanceKm);
                return new RewardNextGoalViewModel
                {
                    Title = $"Naslednji cilj: {targetKm:0} km",
                    Description = $"Še {remainingKm:0.0} km do dosežka {targetKm:0} prehojenih kilometrov.",
                    ProgressPercent = Math.Clamp((int)Math.Round(totalDistanceKm / targetKm * 100), 0, 100),
                    ActionUrl = Url.Action(nameof(Planner), new { dogId }) ?? "/Walks/Planner",
                    ActionLabel = "Načrtuj naslednji sprehod"
                };
            }

            return new RewardNextGoalViewModel
            {
                Title = $"Do nivoja {currentUserLevel.Level + 1}",
                Description = $"Manjka ti še {currentUserLevel.XpRemaining} XP do naslednjega nivoja.",
                ProgressPercent = currentUserLevel.ProgressPercent,
                ActionUrl = Url.Action(nameof(Planner), new { dogId }) ?? "/Walks/Planner",
                ActionLabel = "Načrtuj naslednji sprehod"
            };
        }

        private static GamificationSummaryViewModel BuildGamificationSummary(
            IReadOnlyList<Walk> userCompletedWalks,
            IReadOnlyList<Walk> weeklyCompletedWalks,
            IReadOnlyList<TrashBin> recentContributorBins,
            GamificationStreakInfo canonicalWalkStreak)
        {
            var eightWeekStart = DateTime.UtcNow.Date.AddDays(-55);
            var activeWeeks = userCompletedWalks
                .Where(walk => walk.StartedAt.Date >= eightWeekStart)
                .Select(walk => GetWeekKey(walk.StartedAt.Date))
                .Distinct()
                .Count();

            return new GamificationSummaryViewModel
            {
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

        public double? AccuracyMeters { get; set; }
    }

    public class ToggleStopInput
    {
        public bool Completed { get; set; }
    }
}
