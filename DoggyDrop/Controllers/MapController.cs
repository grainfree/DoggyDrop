using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace DoggyDrop.Controllers
{
    public class MapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IEmailSender _emailSender;
        private readonly INotificationService _notificationService;
        private readonly IGamificationService _gamificationService;
        private readonly IDogProgressionService _dogProgressionService;
        private readonly IMapStampService _mapStampService;
        private readonly IGamificationRewardBuilder _rewardBuilder;
        private readonly IGamificationCalendar _gamificationCalendar;
        private readonly IUserAchievementService _userAchievementService;
        private static readonly IReadOnlyList<FounderArea> FounderAreas =
        [
            new("maribor", "Maribor", 46.5547, 15.6459, 6500),
            new("ljubljana", "Ljubljana", 46.0569, 14.5058, 8500),
            new("celje", "Celje", 46.2397, 15.2677, 5500),
            new("koper", "Koper / Obala", 45.5481, 13.7301, 7000),
            new("kranj", "Kranj", 46.2397, 14.3556, 5500),
            new("ptuj", "Ptuj", 46.4216, 15.8788, 4500),
            new("velenje", "Velenje", 46.3622, 15.1147, 4500),
            new("novo-mesto", "Novo mesto", 45.7998, 15.1771, 5000),
            new("nova-gorica", "Nova Gorica", 45.9561, 13.6482, 5000),
            new("murska-sobota", "Murska Sobota", 46.6606, 16.1664, 4500)
        ];

        public MapController(ApplicationDbContext context,
                             IWebHostEnvironment environment,
                             UserManager<ApplicationUser> userManager,
                             ICloudinaryService cloudinaryService,
                             IEmailSender emailSender,
                             INotificationService notificationService,
                             IGamificationService gamificationService,
                             IDogProgressionService dogProgressionService,
                             IMapStampService mapStampService,
                             IGamificationRewardBuilder rewardBuilder,
                             IGamificationCalendar gamificationCalendar,
                             IUserAchievementService userAchievementService)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
            _emailSender = emailSender;
            _notificationService = notificationService;
            _gamificationService = gamificationService;
            _dogProgressionService = dogProgressionService;
            _mapStampService = mapStampService;
            _rewardBuilder = rewardBuilder;
            _gamificationCalendar = gamificationCalendar;
            _userAchievementService = userAchievementService;
        }

        // 📍 Prikaz obrazca za dodajanje koša
        public async Task<IActionResult> Add(int? walkId = null)
        {
            ViewBag.WalkId = await GetOwnedActiveWalkIdAsync(walkId);
            return View();
        }

        // 📍 Shrani novi koš
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(TrashBinViewModel model, int? walkId = null)
        {
            var returnWalkId = await GetOwnedActiveWalkIdAsync(walkId);
            ViewBag.WalkId = returnWalkId;
            if (!ModelState.IsValid)
                return View(model);

            string? imageUrl = null;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                imageUrl = await _cloudinaryService.UploadTrashBinImageAsync(model.ImageFile);
            }

            var newBin = new TrashBin
            {
                Name = model.Name,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
                ImageUrl = imageUrl,
                DateAdded = DateTime.UtcNow,
                IsApproved = User.IsInRole("Admin"),
                UserId = _userManager.GetUserId(User)
            };

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                _context.TrashBins.Add(newBin);
                await _context.SaveChangesAsync();
                await _gamificationService.AwardXpAsync(
                    newBin.UserId,
                    GamificationConstants.AddedTrashBin,
                    GamificationConstants.AddedTrashBinXp,
                    nameof(TrashBin),
                    newBin.Id.ToString(),
                    "Dodan nov kos");
                await _gamificationService.RecordStreakActivityAsync(newBin.UserId, GamificationStreakConstants.Contribution);

                if (!string.IsNullOrWhiteSpace(newBin.UserId))
                {
                    var submissionCount = await _context.TrashBins.CountAsync(bin => bin.UserId == newBin.UserId);
                    await _userAchievementService.TryUnlockAsync(newBin.UserId, UserAchievementCatalog.BinFirstSubmission, newBin.DateAdded, nameof(TrashBin), newBin.Id.ToString());
                    if (submissionCount >= 10)
                        await _userAchievementService.TryUnlockAsync(newBin.UserId, UserAchievementCatalog.Bin10Submissions, newBin.DateAdded, nameof(TrashBin), newBin.Id.ToString());
                }

                if (newBin.IsApproved)
                {
                    await AwardFounderBadgeIfFirstInAreaAsync(newBin);
                    await NotifyNearbyUsersAboutApprovedBinAsync(newBin, newBin.UserId);
                }

                await transaction.CommitAsync();
            }

            TempData["SuccessMessage"] = User.IsInRole("Admin")
                ? "Kos je bil dodan in je ze viden na zemljevidu."
                : "Hvala! Kos je shranjen in caka na odobritev.";
            return returnWalkId.HasValue
                ? RedirectToAction("Active", "Walks", new { id = returnWalkId.Value })
                : RedirectToAction("Index");
        }

        private async Task<int?> GetOwnedActiveWalkIdAsync(int? walkId)
        {
            if (!walkId.HasValue || walkId.Value <= 0) return null;
            var userId = _userManager.GetUserId(User);
            return await _context.Walks.AnyAsync(walk => walk.Id == walkId.Value && walk.OwnerId == userId && walk.Status == "Active")
                ? walkId
                : null;
        }

        // 🗺️ Glavna stran z zemljevidom
        public async Task<IActionResult> Index()
        {
            var bins = await _context.TrashBins
                .Include(b => b.User)
                .Where(b => b.IsApproved)
                .ToListAsync();

            if (User.Identity?.IsAuthenticated == true)
            {
                var userId = _userManager.GetUserId(User);
                await _gamificationService.AwardDailyLoginAsync(userId);
                var myDogs = await _context.Dogs
                    .Where(dog => dog.OwnerId == userId)
                    .OrderBy(dog => dog.Name)
                    .Select(dog => new
                    {
                        dog.Id,
                        dog.Name,
                        dog.MapIconKey
                    })
                    .ToListAsync();
                var activeWalk = await _context.Walks
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(walk => walk.Dog)
                    .Include(walk => walk.PlannedWalk)
                        .ThenInclude(plan => plan!.RoutePoints)
                    .Include(walk => walk.Points)
                    .Where(walk => walk.OwnerId == userId && walk.Status == "Active")
                    .OrderByDescending(walk => walk.StartedAt)
                    .ThenByDescending(walk => walk.Id)
                    .FirstOrDefaultAsync();

                if (activeWalk != null && WalkStaleness.IsStale(activeWalk, activeWalk.Points ?? [], DateTime.UtcNow))
                {
                    return RedirectToAction("Active", "Walks", new { id = activeWalk.Id });
                }

                ViewBag.MyDogs = myDogs;
                ViewBag.QuickStartDogId = myDogs.Count == 1 ? myDogs[0].Id : (int?)null;
                ViewBag.ActiveWalk = activeWalk;
                ViewBag.NeedsDogOnboarding = myDogs.Count == 0;
                ViewBag.UserDisplayName = (await _userManager.GetUserAsync(User))?.DisplayName
                    ?? User.Identity?.Name
                    ?? "pasjeljubec";
            }
            else
            {
                ViewBag.MyDogs = Array.Empty<object>();
                ViewBag.ActiveWalk = null;
                ViewBag.NeedsDogOnboarding = false;
                ViewBag.UserDisplayName = "pasjeljubec";
            }

            ViewBag.ParkLocations = ParkLocationCatalog.All;

            return View(bins);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ParkVisit([FromBody] ParkVisitInput input)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var dog = await _context.Dogs
                .FirstOrDefaultAsync(candidate => candidate.Id == input.DogId && candidate.OwnerId == userId);

            if (dog == null)
            {
                return NotFound(new { message = "Pes ni najden." });
            }

            var park = ParkLocationCatalog.Find(input.PlaceKey);
            if (park == null)
            {
                return BadRequest(new { message = "Park ni na seznamu podprtih lokacij." });
            }

            var now = _gamificationCalendar.UtcNow.UtcDateTime;
            var placeKey = park.PlaceKey;
            var parkName = park.Name;
            await using var transaction = await _context.Database.BeginTransactionAsync();

            await LockUserParkProgressionAsync(userId);

            var recentDuplicate = await _context.DogParkVisits.AnyAsync(visit =>
                visit.DogId == dog.Id
                && visit.PlaceKey == placeKey
                && visit.VisitedAt >= now.AddHours(-2));

            if (recentDuplicate)
            {
                var recentCount = await _context.DogParkVisits.CountAsync(visit => visit.DogId == dog.Id && visit.PlaceKey == placeKey);
                await transaction.CommitAsync();
                return Json(new { dog.Name, ParkName = parkName, VisitCount = recentCount, Saved = false, Reward = (ParkVisitRewardResultViewModel?)null });
            }

            var visitsBefore = await _context.DogParkVisits
                .AsNoTracking()
                .Where(visit => visit.UserId == userId)
                .ToListAsync();
            var isNewUserDiscovery = visitsBefore.All(visit => visit.PlaceKey != placeKey);
            var isNewForDog = visitsBefore.All(visit => visit.DogId != dog.Id || visit.PlaceKey != placeKey);
            var dogVisitCountBefore = visitsBefore.Count(visit => visit.DogId == dog.Id && visit.PlaceKey == placeKey);
            var uniqueParksBefore = visitsBefore.Select(visit => visit.PlaceKey).Distinct().Count();
            var stampBefore = _mapStampService.BuildCollection(visitsBefore).Stamps.FirstOrDefault(stamp => stamp.PlaceKey == placeKey);
            var userProfile = await _gamificationService.EnsureProfileAsync(userId);
            var userLevelBefore = _gamificationService.CalculateLevelInfo(userProfile.TotalXp);
            var dogProfile = await _dogProgressionService.EnsureProfileAsync(dog.Id);
            var dogLevelBefore = _dogProgressionService.CalculateLevelInfo(dogProfile.TotalXp);
            var dogProfileBefore = _rewardBuilder.Snapshot(dogProfile);
            var explorerStreakBefore = await _context.UserStreaks.AsNoTracking()
                .FirstOrDefaultAsync(streak => streak.UserId == userId && streak.StreakType == GamificationStreakConstants.Explorer);
            var explorerDaysBefore = _gamificationService
                .GetEffectiveStreak(explorerStreakBefore, GamificationStreakConstants.Explorer)
                .EffectiveCurrentDays;

            var visit = new DogParkVisit
            {
                DogId = dog.Id,
                UserId = userId,
                ParkName = parkName,
                Area = park.Area,
                Address = park.Address,
                PlaceKey = placeKey,
                Latitude = park.Latitude,
                Longitude = park.Longitude,
                VisitedAt = now
            };
            _context.DogParkVisits.Add(visit);

            await _context.SaveChangesAsync();
            var userXpEvent = isNewUserDiscovery
                ? await _gamificationService.AwardXpAsync(userId, GamificationConstants.VisitNewPark,
                    GamificationConstants.VisitNewParkXp, nameof(DogParkVisit), placeKey, "Obiskan nov park")
                : null;
            var explorerStreak = await _gamificationService.RecordStreakActivityAtAsync(userId, GamificationStreakConstants.Explorer, now);
            var dogXpEvent = isNewForDog
                ? await _dogProgressionService.AwardXpAsync(dog.Id, "ParkVisit", 35,
                    new DogProgressionStatBoost { Adventure = 10, Social = 8, Forest = 12 }, nameof(DogParkVisit), $"{dog.Id}:{placeKey}", "Obisk pasjega parka")
                : null;

            var userLevelAfter = await _gamificationService.GetLevelInfoAsync(userId);
            var dogProfileAfter = await _context.DogProgressionProfiles.AsNoTracking().SingleAsync(profile => profile.DogId == dog.Id);
            var dogLevelAfter = _dogProgressionService.CalculateLevelInfo(dogProfileAfter.TotalXp);
            var visitsAfter = await _context.DogParkVisits.AsNoTracking().Where(visit => visit.UserId == userId).ToListAsync();
            var stampAfter = _mapStampService.BuildCollection(visitsAfter).Stamps.Single(stamp => stamp.PlaceKey == placeKey);
            var uniqueParksAfter = visitsAfter.Select(visit => visit.PlaceKey).Distinct().Count();
            var explorerUnlock = uniqueParksAfter >= 5
                ? await _userAchievementService.TryUnlockAsync(userId, UserAchievementCatalog.Explorer5Places, now, nameof(DogParkVisit), visit.Id.ToString())
                : null;
            var achievements = explorerUnlock?.NewlyUnlocked == true
                ? new[] { ToRewardAchievement(explorerUnlock.AchievementKey) }
                : [];
            var nextGoal = uniqueParksAfter < 5
                ? new RewardNextGoalViewModel
                {
                    Title = "Raziskovalec parkov",
                    Description = $"Obišči še {5 - uniqueParksAfter} nov park za dosežek Obiskanih 5 parkov.",
                    ProgressPercent = Math.Clamp((int)Math.Round(uniqueParksAfter / 5d * 100), 0, 100),
                    ActionUrl = Url.Action(nameof(Index), "Map") ?? "/Map",
                    ActionLabel = "Razišči zemljevid"
                }
                : new RewardNextGoalViewModel
                {
                    Title = $"Do nivoja {userLevelAfter.Level + 1}",
                    Description = $"Manjka ti še {userLevelAfter.XpRemaining} XP do naslednjega nivoja.",
                    ProgressPercent = userLevelAfter.ProgressPercent,
                    ActionUrl = Url.Action(nameof(Index), "Map") ?? "/Map",
                    ActionLabel = "Razišči zemljevid"
                };

            var reward = new ParkVisitRewardResultViewModel
            {
                IsNewUserDiscovery = isNewUserDiscovery,
                IsNewForDog = isNewForDog,
                ParkName = parkName,
                VisitCount = dogVisitCountBefore + 1,
                Progression = new GamificationRewardResultViewModel
                {
                    UserReward = _rewardBuilder.BuildUserReward(userXpEvent, userLevelBefore, userLevelAfter),
                    DogReward = _rewardBuilder.BuildDogReward(dog, dogXpEvent, dogLevelBefore, dogLevelAfter, dogProfileBefore, dogProfileAfter),
                    StreakReward = _rewardBuilder.BuildStreakReward(
                        _gamificationService.GetEffectiveStreak(explorerStreak, GamificationStreakConstants.Explorer),
                        explorerDaysBefore,
                        includeUnchanged: false),
                    UnlockedAchievements = achievements,
                    NextGoal = nextGoal
                },
                Stamp = new MapStampRewardViewModel
                {
                    LocationName = stampAfter.Name,
                    Rarity = stampAfter.Rarity,
                    PreviousRarity = stampBefore?.Rarity,
                    IsNew = stampBefore == null,
                    WasUpgraded = stampBefore != null && !string.Equals(stampBefore.Rarity, stampAfter.Rarity, StringComparison.Ordinal)
                }
            };

            await transaction.CommitAsync();

            return Json(new
            {
                dog.Name,
                ParkName = parkName,
                VisitCount = reward.VisitCount,
                Saved = true,
                Reward = reward
            });
        }

        private async Task LockUserParkProgressionAsync(string userId)
        {
            if (_context.Database.IsNpgsql())
            {
                // PostgreSQL READ COMMITTED holds this row lock until transaction completion.
                await _context.Users
                    .FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR UPDATE")
                    .SingleAsync();
                return;
            }

            // SQLite functional tests serialize writers at database scope.
            await _context.Users
                .Where(user => user.Id == userId)
                .ExecuteUpdateAsync(update => update.SetProperty(user => user.AccessFailedCount, user => user.AccessFailedCount));
        }

        // ✅ Upravljanje - prikaz neodobrenih predlogov
        [Authorize(Roles = "Admin")]
        public IActionResult Manage()
        {
            var pendingBins = _context.TrashBins
                .Include(b => b.User) // ✅ vključimo uporabnika
                .Where(b => !b.IsApproved)
                .OrderByDescending(b => b.DateAdded)
                .ToList();

            return View(pendingBins);
        }

        // ✅ Potrdi predlog
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Approve(int id)
        {
            var bin = _context.TrashBins.Find(id);
            if (bin != null)
            {
                bin.IsApproved = true;
                await _context.SaveChangesAsync();
                await AwardFounderBadgeIfFirstInAreaAsync(bin);

                if (!string.IsNullOrWhiteSpace(bin.UserId))
                {
                    await _notificationService.CreateUniqueRecentAsync(
                        bin.UserId,
                        "BinApproved",
                        "Tvoj pasji kos je odobren",
                        $"{bin.Name} je zdaj viden na DoggyDrop zemljevidu.",
                        Url.Action(nameof(MyBins), "Map"),
                        withinHours: 24 * 14);
                    await _gamificationService.AwardXpAsync(
                        bin.UserId,
                        GamificationConstants.ApprovedTrashBin,
                        GamificationConstants.ApprovedTrashBinXp,
                        nameof(TrashBin),
                        bin.Id.ToString(),
                        "Kos je bil odobren");
                }

                await NotifyNearbyUsersAboutApprovedBinAsync(bin, bin.UserId);
            }

            return RedirectToAction("Manage");
        }

        // Moderator deletion remains available; contributor proposals are retained for reward integrity.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Reject(int id, string? returnTo)
        {
            if (User.IsInRole("Admin"))
            {
                var bin = await _context.TrashBins.FindAsync(id);
                if (bin != null)
                {
                    _context.TrashBins.Remove(bin);
                    await _context.SaveChangesAsync();
                }
                return string.Equals(returnTo, "mybins", StringComparison.OrdinalIgnoreCase)
                    ? RedirectToAction(nameof(MyBins))
                    : RedirectToAction(nameof(Manage));
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Forbid();

            var ownsBin = await _context.TrashBins.AsNoTracking()
                .AnyAsync(bin => bin.Id == id && bin.UserId == userId);
            if (!ownsBin) return NotFound();

            TempData["ErrorMessage"] = "Predloga trenutno ni mogoče izbrisati. Za pomoč se obrni na moderatorja.";

            return RedirectToAction(nameof(MyBins));
        }

        // 🔥 Admin ročno brisanje
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null)
                return NotFound();

            _context.TrashBins.Remove(bin);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Koš je bil uspešno izbrisan!";
            return RedirectToAction("Manage");
        }

        // 👤 Moji predlogi
        [Authorize]
        public async Task<IActionResult> MyBins()
        {
            var userId = _userManager.GetUserId(User);
            var myBins = await _context.TrashBins
                .Where(b => b.UserId == userId)
                .ToListAsync();

            ViewBag.BinCount = myBins.Count;
            return View(myBins);
        }

        // 📍 API: Najdi najbližji koš
        [HttpGet]
        public IActionResult GetNearestBin(double latitude, double longitude)
        {
            var nearest = _context.TrashBins
                .Where(b => b.IsApproved)
                .OrderBy(b => Math.Pow(b.Latitude - latitude, 2) + Math.Pow(b.Longitude - longitude, 2))
                .FirstOrDefault();

            if (nearest == null) return NotFound();

            return Json(new
            {
                nearest.Id,
                nearest.Name,
                nearest.Latitude,
                nearest.Longitude,
                ImageUrl = nearest.FullImageUrl,
                ReliabilityScore = GetBinReliabilityScore(nearest)
            });
        }

        [HttpGet]
        public IActionResult GetBestBin(double latitude, double longitude)
        {
            var best = _context.TrashBins
                .Where(b => b.IsApproved)
                .AsEnumerable()
                .Select(bin => new
                {
                    Bin = bin,
                    DistanceMeters = GetDistanceMeters(latitude, longitude, bin.Latitude, bin.Longitude),
                    ReliabilityScore = GetBinReliabilityScore(bin)
                })
                .OrderBy(item => item.DistanceMeters * 0.65 - item.ReliabilityScore * 9)
                .FirstOrDefault();

            if (best == null)
            {
                return NotFound();
            }

            return Json(new
            {
                best.Bin.Id,
                best.Bin.Name,
                best.Bin.Latitude,
                best.Bin.Longitude,
                ImageUrl = best.Bin.FullImageUrl,
                best.DistanceMeters,
                best.ReliabilityScore
            });
        }

        // 📍 API: Vsi odobreni koši
        [HttpGet]
        public IActionResult FindNearest()
        {
            var bins = _context.TrashBins
                .Where(b => b.IsApproved)
                .ToList()
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Latitude,
                    b.Longitude,
                    ImageUrl = b.FullImageUrl
                })
                .ToList();

            return Json(bins);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> BinAction(int id, string action)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null || !bin.IsApproved)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;

            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "used":
                    bin.UsedCount++;
                    bin.LastUsedAt = now;
                    break;
                case "full":
                    bin.FullReports++;
                    bin.LastReportedAt = now;
                    break;
                case "missing":
                    bin.MissingReports++;
                    bin.LastReportedAt = now;
                    break;
                case "useful":
                    bin.UsefulVotes++;
                    break;
                case "not-useful":
                    bin.NotUsefulVotes++;
                    break;
                default:
                    return BadRequest(new { message = "Neznana akcija." });
            }

            await _context.SaveChangesAsync();
            if (string.Equals(action, "useful", StringComparison.OrdinalIgnoreCase))
            {
                await _gamificationService.AwardXpAsync(
                    _userManager.GetUserId(User),
                    GamificationConstants.HelpfulVote,
                    GamificationConstants.HelpfulVoteXp,
                    nameof(TrashBin),
                    $"{bin.Id}:useful",
                    "Koristen glas za kos");
                await _gamificationService.RecordStreakActivityAsync(_userManager.GetUserId(User), GamificationStreakConstants.Contribution);
            }

            return Json(new
            {
                bin.Id,
                bin.UsedCount,
                bin.FullReports,
                bin.MissingReports,
                bin.UsefulVotes,
                bin.NotUsefulVotes,
                ReliabilityScore = GetBinReliabilityScore(bin),
                LastUsedAt = bin.LastUsedAt?.ToString("dd.MM.yyyy HH:mm"),
                LastReportedAt = bin.LastReportedAt?.ToString("dd.MM.yyyy HH:mm")
            });
        }

        // ✏️ Prikaz obrazca za urejanje koša
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null)
                return NotFound();

            var model = new TrashBinEditViewModel
            {
                Id = bin.Id,
                Name = bin.Name,
                Latitude = bin.Latitude,
                Longitude = bin.Longitude,
                CurrentImageUrl = bin.FullImageUrl
            };

            return View(model);
        }

        // ✏️ Shrani spremembe koša
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TrashBinEditViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var bin = await _context.TrashBins.FindAsync(model.Id);
            if (bin == null)
                return NotFound();

            bin.Name = model.Name;
            bin.Latitude = model.Latitude;
            bin.Longitude = model.Longitude;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var imageUrl = await _cloudinaryService.UploadTrashBinImageAsync(model.ImageFile);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    bin.ImageUrl = imageUrl;
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Hvala za vaš prispevek! Vaš koš je bil uspešno dodan. Administrator ga bo kmalu pregledal. 🐾";
            return RedirectToAction("Manage");
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

        private async Task AwardFounderBadgeIfFirstInAreaAsync(TrashBin bin)
        {
            if (string.IsNullOrWhiteSpace(bin.UserId))
            {
                return;
            }

            var area = ResolveFounderArea(bin.Latitude, bin.Longitude);
            var approvedAreaBins = await _context.TrashBins
                .Where(candidate => candidate.Id != bin.Id && candidate.IsApproved)
                .Select(candidate => new { candidate.Latitude, candidate.Longitude })
                .ToListAsync();
            var hasEarlierApprovedBin = approvedAreaBins.Any(candidate =>
                GetDistanceMeters(candidate.Latitude, candidate.Longitude, area.Latitude, area.Longitude) <= area.RadiusMeters);

            if (hasEarlierApprovedBin)
            {
                return;
            }

            var alreadyClaimed = await _context.FounderBadges.AnyAsync(badge =>
                badge.AreaKey == area.Key && badge.BadgeType == "ExplorerFounder");

            if (alreadyClaimed)
            {
                return;
            }

            _context.FounderBadges.Add(new FounderBadge
            {
                UserId = bin.UserId,
                AreaKey = area.Key,
                AreaName = area.Name,
                BadgeType = "ExplorerFounder",
                TrashBinId = bin.Id,
                UnlockedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await _gamificationService.AwardXpAsync(
                bin.UserId,
                GamificationConstants.FounderBadge,
                GamificationConstants.FounderBadgeXp,
                nameof(FounderBadge),
                area.Key,
                $"Founder explorer za {area.Name}");
            await _notificationService.CreateUniqueRecentAsync(
                bin.UserId,
                $"FounderBadge:{area.Key}",
                $"Founder Explorer: {area.Name}",
                $"Prvi si dodal odobren pasji koš za območje {area.Name}. Ta founder badge ostane na tvojem profilu.",
                Url.Action(nameof(HomeController.UserProfile), "Home"),
                withinHours: 24 * 365);
        }

        private static FounderArea ResolveFounderArea(double latitude, double longitude)
        {
            return FounderAreas
                .Select(area => area with
                {
                    DistanceMeters = GetDistanceMeters(latitude, longitude, area.Latitude, area.Longitude)
                })
                .Where(area => area.DistanceMeters <= area.RadiusMeters)
                .OrderBy(area => area.DistanceMeters)
                .FirstOrDefault()
                ?? new FounderArea(
                    BuildAreaKey(latitude, longitude),
                    "Novo DoggyDrop območje",
                    latitude,
                    longitude,
                    2500,
                    0);
        }

        private static string BuildAreaKey(double latitude, double longitude)
        {
            return $"area-{Math.Round(latitude, 2):0.00}-{Math.Round(longitude, 2):0.00}".Replace(',', '.');
        }

        private static RewardAchievementViewModel ToRewardAchievement(string key)
        {
            var definition = UserAchievementCatalog.Get(key);
            return new RewardAchievementViewModel { Key = definition.Key, Name = definition.DisplayName, Description = definition.Description };
        }

        private async Task NotifyNearbyUsersAboutApprovedBinAsync(TrashBin bin, string? excludeUserId)
        {
            var candidateUsers = new HashSet<string>(StringComparer.Ordinal);
            var pointCutoff = DateTime.UtcNow.AddDays(-30);
            var recentWalkPoints = await _context.WalkPoints
                .Include(point => point.Walk)
                .Where(point => point.RecordedAt >= pointCutoff && point.Walk != null)
                .ToListAsync();

            foreach (var point in recentWalkPoints)
            {
                var ownerId = point.Walk?.OwnerId;
                if (string.IsNullOrWhiteSpace(ownerId) || ownerId == excludeUserId)
                {
                    continue;
                }

                if (GetDistanceMeters(bin.Latitude, bin.Longitude, point.Latitude, point.Longitude) <= 4000)
                {
                    candidateUsers.Add(ownerId);
                }
            }

            var visitCutoff = DateTime.UtcNow.AddDays(-60);
            var recentParkVisits = await _context.DogParkVisits
                .Where(visit => visit.VisitedAt >= visitCutoff)
                .ToListAsync();

            foreach (var visit in recentParkVisits)
            {
                if (string.IsNullOrWhiteSpace(visit.UserId) || visit.UserId == excludeUserId)
                {
                    continue;
                }

                if (GetDistanceMeters(bin.Latitude, bin.Longitude, visit.Latitude, visit.Longitude) <= 4000)
                {
                    candidateUsers.Add(visit.UserId);
                }
            }

            foreach (var userId in candidateUsers.Take(20))
            {
                await _notificationService.CreateUniqueRecentAsync(
                    userId,
                    "NewBinNearby",
                    "Nov pasji kos v tvoji okolici",
                    $"{bin.Name} je zdaj na voljo blizu tvojih pogostih sprehodov.",
                    Url.Action(nameof(Index), "Map"),
                    withinHours: 48);
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

        private static double ToRadians(double value)
        {
            return value * Math.PI / 180;
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

        private sealed record FounderArea(
            string Key,
            string Name,
            double Latitude,
            double Longitude,
            double RadiusMeters,
            double DistanceMeters = 0);
    }

    public class ParkVisitInput
    {
        public int DogId { get; set; }

        public string PlaceKey { get; set; } = string.Empty;
    }
}
