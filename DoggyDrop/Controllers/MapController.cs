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
        private readonly IUserAchievementService _userAchievementService;
        private readonly NearbyDiscoveryService _nearbyDiscoveryService;
        private readonly PlaceLogoCloudName _placeLogoCloud;
        private readonly TimeProvider _clock;
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
                             IUserAchievementService userAchievementService,
                             NearbyDiscoveryService? nearbyDiscoveryService = null,
                             PlaceLogoCloudName? placeLogoCloud = null, TimeProvider? clock = null)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
            _emailSender = emailSender;
            _notificationService = notificationService;
            _gamificationService = gamificationService;
            _userAchievementService = userAchievementService;
            _nearbyDiscoveryService = nearbyDiscoveryService ?? new NearbyDiscoveryService(context);
            _placeLogoCloud = placeLogoCloud ?? new PlaceLogoCloudName(null);
            _clock = clock ?? TimeProvider.System;
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
        [RequestSizeLimit(BinPhotoUploadPolicy.MaxBytes + 65536)]
        [BinSubmissionLimit]
        public async Task<IActionResult> Add(TrashBinViewModel model, int? walkId = null)
        {
            var returnWalkId = await GetOwnedActiveWalkIdAsync(walkId);
            ViewBag.WalkId = returnWalkId;
            if (!ModelState.IsValid)
                return View(model);

            if (!BinCommunityRules.Coordinates(model.Latitude, model.Longitude)) ModelState.AddModelError("", "Označi veljavno lokacijo v Sloveniji.");
            else if (await BinCommunityRules.DuplicateAsync(_context, model.Latitude, model.Longitude)) ModelState.AddModelError("", "V razdalji do vključno 20 m že obstaja predlog ali koš. Preveri zemljevid.");
            if (!ModelState.IsValid) return View(model);
            string? imageUrl = null;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                imageUrl = await UploadBinPhotoAsync(model.ImageFile);
                if (!ModelState.IsValid) return View(model);
            }

            var createdAt = DateTime.UtcNow;
            var approvedImmediately = User.IsInRole("Admin");
            var newBin = new TrashBin
            {
                Name = model.Name,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
                ImageUrl = imageUrl,
                DateAdded = createdAt,
                IsApproved = approvedImmediately,
                ApprovedAt = approvedImmediately ? createdAt : null,
                UserId = _userManager.GetUserId(User)
            };

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                await BinCommunityRules.LockAsync(_context);
                if (await BinCommunityRules.DuplicateAsync(_context, model.Latitude, model.Longitude)) {
                    // Another submit won. Retain any upload for safe orphan reconciliation.
                    ModelState.AddModelError("", "Medtem je bil dodan bližnji koš. Preveri zemljevid.");
                    return View(model);
                }
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
                    await _nearbyDiscoveryService.NotifyForApprovedBinAsync(newBin);
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
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            var bins = await _context.TrashBins
                .Include(b => b.DataSource)
                .PublicBins()
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

            var managedPlaces = await _context.Places.AsNoTracking()
                .Where(place => place.IsActive && PlaceCategories.Supported.Contains(place.Category))
                .WithFeatured(_clock.GetUtcNow().UtcDateTime)
                .OrderBy(row => row.Place.Id)
                .Select(row => new { row.Place.Id, row.Place.Name, row.Place.Category,
                    row.Place.Latitude, row.Place.Longitude, row.Place.Address, row.Place.LogoUrl, row.IsCurrentlyFeatured })
                .ToListAsync();
            var trust = new InfrastructureTrust(_context, _clock);
            ViewBag.BinTrust = await trust.BinsAsync();
            var waterTrust = await trust.WaterAsync();
            ViewBag.WaterPoints = (await WaterPoints.LoadAsync(_context.WaterPoints))
                .Select(p => p with { Trust = waterTrust.GetValueOrDefault(p.Id, InfrastructureTrust.Empty) }).ToList();
            ViewBag.ManagedPlaces = managedPlaces
                .Select(place =>
                {
                    var category = PlaceCategories.Get(place.Category);
                    return new PlaceMapItem(place.Id, place.Name, place.Category,
                        place.Latitude, place.Longitude, place.Address,
                        PlaceCategories.PublicLogo(place.Category, place.LogoUrl, _placeLogoCloud.Value),
                        category.Label, category.Key, category.IconClass, category.IsCommercial, place.IsCurrentlyFeatured);
                })
                .ToList();

            return View(bins);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> ParkVisit([FromBody] ParkVisitInput input)
        {
            // Retired current check-in entry point. Historical catalog keys and
            // stored visits remain readable; no new visits or rewards are written.
            return Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status410Gone,
                new { message = "Obiski starih lokacij niso več na voljo. Zgodovina obiskov je ohranjena." }));
        }

        // ✅ Upravljanje - prikaz neodobrenih predlogov
        [Authorize(Roles = "Admin")]
        public IActionResult Manage()
        {
            var pendingBins = _context.TrashBins
                .Include(b => b.DataSource)
                .Include(b => b.User) // ✅ vključimo uporabnika
                .Where(b => !b.IsApproved && !b.IsRetired && !b.IsRejected)
                .OrderByDescending(b => b.DateAdded)
                .ToList();

            return View(pendingBins);
        }

        // ✅ Potrdi predlog
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Approve(int id, string? snapshot = null)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await BinCommunityRules.LockAsync(_context);
            var pending = await _context.TrashBins.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id && !b.IsApproved && !b.IsRejected && !b.IsRetired);
            if (pending == null) return RedirectToAction(nameof(Manage));
            if (snapshot != BinCommunityRules.Snapshot(pending)) return Conflict("Predlog je bil medtem spremenjen. Ponovno odpri pregled.");
            if (await BinCommunityRules.DuplicateAsync(_context, pending.Latitude, pending.Longitude, id)) return Conflict("Koš ima možnega dvojnika do vključno 20 m. Potrebna je ročna razrešitev.");
            var approvedAt = DateTime.UtcNow;
            var evidenceVersion = Guid.NewGuid();
            var changed = await _context.TrashBins
                .Where(bin => bin.Id == id && !bin.IsApproved && !bin.IsRetired && !bin.IsRejected && bin.Latitude == pending.Latitude && bin.Longitude == pending.Longitude
                    && bin.Name == pending.Name && bin.ImageUrl == pending.ImageUrl && bin.DataSourceId == pending.DataSourceId && bin.UserId == pending.UserId)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(bin => bin.IsApproved, true)
                    .SetProperty(bin => bin.EvidenceVersion, evidenceVersion)
                    .SetProperty(bin => bin.ApprovedAt, approvedAt));
            if (changed == 0) return Conflict("Predlog je bil medtem spremenjen. Ponovno odpri pregled.");

            var bin = await _context.TrashBins.AsNoTracking().SingleAsync(item => item.Id == id);
            await AwardFounderBadgeIfFirstInAreaAsync(bin);

            if (!string.IsNullOrWhiteSpace(bin.UserId))
            {
                await CreateBinApprovedNotificationAsync(bin);
                await _gamificationService.AwardXpAsync(
                    bin.UserId,
                    GamificationConstants.ApprovedTrashBin,
                    GamificationConstants.ApprovedTrashBinXp,
                    nameof(TrashBin),
                    bin.Id.ToString(),
                    "Kos je bil odobren");
            }

            await _nearbyDiscoveryService.NotifyForApprovedBinAsync(bin);
            await transaction.CommitAsync();

            return RedirectToAction("Manage");
        }

        private async Task CreateBinApprovedNotificationAsync(TrashBin bin)
        {
            var sourceKey = $"BinApproved:{bin.Id}";
            var body = $"{bin.Name} je zdaj viden na DoggyDrop zemljevidu.";
            var link = Url.Action(nameof(MyBins), "Map");
            await _context.Database.ExecuteSqlInterpolatedAsync($$"""
                INSERT INTO "UserNotifications" ("UserId", "Type", "Title", "Body", "LinkUrl", "SourceKey", "IsRead", "CreatedAt")
                VALUES ({{bin.UserId}}, 'BinApproved', 'Tvoj pasji kos je odobren',
                    {{body}}, {{link}}, {{sourceKey}}, {{false}}, {{DateTime.UtcNow}})
                ON CONFLICT ("UserId", "SourceKey") DO NOTHING
                """);
        }

        // Rejection retains new-bin evidence; approved infrastructure uses the separate retirement flow.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Reject(int id, string? returnTo, string? snapshot = null)
        {
            if (User.IsInRole("Admin"))
            {
                var bin = await _context.TrashBins.FindAsync(id);
                if (bin != null)
                {
                    if (bin.IsApproved || bin.IsRetired) return Conflict("Odobren koš upokoji v Admin pregledu; zavrnitev je namenjena novim predlogom.");
                    if (bin.IsRejected) return RedirectToAction(nameof(Manage));
                    if (snapshot != BinCommunityRules.Snapshot(bin)) return Conflict("Predlog je bil medtem spremenjen. Ponovno odpri pregled.");
                    bin.IsRejected = true; bin.RejectedAt = DateTime.UtcNow;
                    try { await _context.SaveChangesAsync(); }
                    catch (DbUpdateConcurrencyException) { return Conflict("Predlog je bil medtem spremenjen. Ponovno odpri pregled."); }
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

            if (await _context.BinContributions.AnyAsync(c => c.BinId == id)) return Conflict("Koš ima zgodovino prispevkov. Uporabi upokojitev.");
            _context.TrashBins.Remove(bin);
            try { await _context.SaveChangesAsync(); }
            catch (DbUpdateException) { return Conflict("Koš se je spremenil ali ima povezano zgodovino. Osveži pregled in uporabi upokojitev."); }

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
                .PublicBins()
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
                .PublicBins()
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
                .PublicBins()
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BinAction(int id, [FromForm] string binAction)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null || !bin.IsApproved || bin.IsRetired)
            {
                return NotFound();
            }

            var now = DateTime.UtcNow;

            switch ((binAction ?? string.Empty).Trim().ToLowerInvariant())
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
                    return Conflict(new { message = "Težavo oddaj v pregled.", contributionUrl = $"/BinContributions/Create/{id}" });
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
            if (string.Equals(binAction, "useful", StringComparison.OrdinalIgnoreCase))
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

            ViewData["BinPhotoUrl"] = bin.ImageUrl;
            var model = new TrashBinEditViewModel
            {
                Id = bin.Id,
                Name = bin.Name,
                Latitude = bin.Latitude,
                Longitude = bin.Longitude,
                CurrentImageUrl = bin.FullImageUrl,
                Snapshot = BinCommunityRules.Snapshot(bin)
            };

            return View(model);
        }

        // ✏️ Shrani spremembe koša
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(BinPhotoUploadPolicy.MaxBytes + 65536)]
        public async Task<IActionResult> Edit(TrashBinEditViewModel model)
        {
            var bin = await _context.TrashBins.FindAsync(model.Id);
            if (bin == null)
                return NotFound();

            ViewData["BinPhotoUrl"] = bin.ImageUrl;
            model.CurrentImageUrl = bin.FullImageUrl;
            if (!ModelState.IsValid) return View(model);
            if (model.Snapshot != BinCommunityRules.Snapshot(bin)) return Conflict("Koš je bil medtem spremenjen. Ponovno odpri urejanje.");
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await BinCommunityRules.LockAsync(_context);
            if (model.Latitude != bin.Latitude || model.Longitude != bin.Longitude)
            {
                if (!BinCommunityRules.Coordinates(model.Latitude, model.Longitude) || await BinCommunityRules.DuplicateAsync(_context, model.Latitude, model.Longitude, bin.Id))
                { ModelState.AddModelError("", "Lokacija ni veljavna ali ima možnega dvojnika do vključno 20 m."); return View(model); }
            }
            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var imageUrl = await UploadBinPhotoAsync(model.ImageFile);
                if (!ModelState.IsValid) return View(model);
                bin.ImageUrl = imageUrl;
            }

            bin.Name = model.Name;
            bin.Latitude = model.Latitude;
            bin.Longitude = model.Longitude;
            try { await _context.SaveChangesAsync(); await transaction.CommitAsync(); }
            catch (DbUpdateConcurrencyException) { return Conflict("Koš je bil medtem spremenjen. Ponovno odpri urejanje."); }
            TempData["SuccessMessage"] = "Hvala za vaš prispevek! Vaš koš je bil uspešno dodan. Administrator ga bo kmalu pregledal. 🐾";
            return RedirectToAction("Manage");
        }

        private async Task<string?> UploadBinPhotoAsync(IFormFile file)
        {
            if (file.Length <= BinPhotoUploadPolicy.MaxBytes)
            {
                try
                {
                    var url = await _cloudinaryService.UploadTrashBinImageAsync(file);
                    if (!string.IsNullOrWhiteSpace(url)) return url;
                }
                catch (Exception)
                {
                    HttpContext.RequestServices.GetService<ILogger<MapController>>()?.LogWarning("Bin photo upload failed.");
                }
            }
            ModelState.AddModelError("ImageFile", BinPhotoUploadPolicy.Error + " Če nalaganje ne uspe, poskusi znova.");
            return null;
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
