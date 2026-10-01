using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace DoggyDrop.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IEmailSender _emailSender;
        private readonly ApplicationDbContext _context;
        private readonly IGamificationService _gamificationService;
        private readonly ISeasonalEventService _seasonalEventService;
        private readonly ILocalLeaderboardService _localLeaderboardService;
        private readonly IMapStampService _mapStampService;
        private readonly IUserAchievementService _userAchievementService;

        public HomeController(
            ILogger<HomeController> logger,
            UserManager<ApplicationUser> userManager,
            ICloudinaryService cloudinaryService,
            IEmailSender emailSender,
            ApplicationDbContext context,
            IGamificationService gamificationService,
            ISeasonalEventService seasonalEventService,
            ILocalLeaderboardService localLeaderboardService,
            IMapStampService mapStampService,
            IUserAchievementService userAchievementService)
        {
            _logger = logger;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
            _emailSender = emailSender;
            _context = context;
            _gamificationService = gamificationService;
            _seasonalEventService = seasonalEventService;
            _localLeaderboardService = localLeaderboardService;
            _mapStampService = mapStampService;
            _userAchievementService = userAchievementService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Help()
        {
            return View();
        }

        public IActionResult PwaHelp()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Settings()
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();

            var zone = await _context.PrivacyZones.AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == userId);
            var discovery = await _context.NearbyDiscoveryPreferences.AsNoTracking()
                .SingleOrDefaultAsync(item => item.UserId == userId);
            return View(new PrivacyZoneSettingsViewModel
            {
                IsEnabled = zone != null,
                Latitude = zone?.Latitude,
                Longitude = zone?.Longitude,
                RadiusMeters = zone?.RadiusMeters ?? 300,
                Discovery = new NearbyDiscoverySettingsViewModel
                {
                    IsEnabled = discovery != null,
                    Latitude = discovery?.Latitude,
                    Longitude = discovery?.Longitude,
                    RadiusMeters = discovery?.RadiusMeters ?? 3000,
                    BinsEnabled = discovery?.BinsEnabled ?? true
                }
            });
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveNearbyDiscovery(NearbyDiscoverySettingsInput input)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();

            if (!input.Enabled)
            {
                await _context.NearbyDiscoveryPreferences
                    .Where(item => item.UserId == userId).ExecuteDeleteAsync();
                TempData["NearbyDiscoverySuccessMessage"] = "Obvestila v bližini so izklopljena in izbrana lokacija je izbrisana.";
                return RedirectToAction(nameof(Settings), null, null, "nearby-notifications");
            }

            if (!ModelState.IsValid || !input.BinsEnabled ||
                input.RadiusMeters is not (1000 or 3000 or 5000 or 10000) ||
                !double.TryParse(input.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(input.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                !NearbyDiscoveryService.ValidCoordinate(latitude, longitude))
            {
                TempData["NearbyDiscoveryErrorMessage"] = "Izberi veljavno lokacijo, velikost območja in pasje koše. Prejšnja nastavitev je ohranjena.";
                return RedirectToAction(nameof(Settings), null, null, "nearby-notifications");
            }

            var preference = await _context.NearbyDiscoveryPreferences
                .SingleOrDefaultAsync(item => item.UserId == userId);
            if (preference == null)
            {
                preference = new NearbyDiscoveryPreference { UserId = userId, EnabledAt = DateTime.UtcNow };
                _context.NearbyDiscoveryPreferences.Add(preference);
            }

            preference.Latitude = latitude;
            preference.Longitude = longitude;
            preference.RadiusMeters = input.RadiusMeters;
            preference.BinsEnabled = input.BinsEnabled;
            await _context.SaveChangesAsync();
            TempData["NearbyDiscoverySuccessMessage"] = "Obvestila v bližini so shranjena. Obveščali te bomo o na novo odobrenih koših.";
            return RedirectToAction(nameof(Settings), null, null, "nearby-notifications");
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePrivacyZone(PrivacyZoneSettingsInput input)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId)) return Challenge();

            if (!input.Enabled)
            {
                await _context.PrivacyZones.Where(item => item.UserId == userId).ExecuteDeleteAsync();
                TempData["PrivacyZoneSuccessMessage"] = "Zasebno območje je izklopljeno in lokacija izbrisana.";
                return RedirectToAction(nameof(Settings), null, null, "privacy-zone");
            }

            if (!ModelState.IsValid || input.RadiusMeters is not (200 or 300 or 500 or 1000) ||
                !double.TryParse(input.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(input.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
                !double.IsFinite(latitude) || !double.IsFinite(longitude) ||
                latitude is < -90 or > 90 || longitude is < -180 or > 180)
            {
                TempData["PrivacyZoneErrorMessage"] = "Izberi veljavno lokacijo in velikost zasebnega območja. Prejšnja nastavitev je ohranjena.";
                return RedirectToAction(nameof(Settings), null, null, "privacy-zone");
            }

            var zone = await _context.PrivacyZones.SingleOrDefaultAsync(item => item.UserId == userId);
            if (zone == null)
            {
                zone = new PrivacyZone { UserId = userId };
                _context.PrivacyZones.Add(zone);
            }

            zone.Latitude = latitude;
            zone.Longitude = longitude;
            zone.RadiusMeters = input.RadiusMeters;
            await _context.SaveChangesAsync();
            TempData["PrivacyZoneSuccessMessage"] = "Zasebno območje je shranjeno.";
            return RedirectToAction(nameof(Settings), null, null, "privacy-zone");
        }

        [Authorize]
        public async Task<IActionResult> Community()
        {
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            // Only aggregate activity leaves this query. Individual walks/photos stay in
            // the owner's history; a friendship does not constitute deliberate sharing.
            var weeklyActivity = await _context.Walks.AsNoTracking()
                .Where(walk => walk.Status == "Completed" && walk.StartedAt >= weekStart)
                .Select(walk => new { walk.DistanceMeters, walk.DogId })
                .ToListAsync();
            var binPhotoGallery = (await _context.TrashBins
                .Include(bin => bin.User)
                .PublicBins().Where(bin => !string.IsNullOrWhiteSpace(bin.ImageUrl))
                .OrderByDescending(bin => bin.DateAdded)
                .Take(12)
                .ToListAsync())
                .Select(bin => new CommunityBinPhotoItem
                {
                    BinId = bin.Id,
                    BinName = bin.Name,
                    ImageUrl = bin.FullImageUrl!,
                    ContributorName = GetDisplayName(bin.User),
                    DateAdded = bin.DateAdded
                })
                .Where(bin => !string.IsNullOrWhiteSpace(bin.ImageUrl))
                .DistinctBy(bin => bin.BinId)
                .ToList();

            return View(new CommunityViewModel
            {
                LocalLeaderboards = MapLocalLeaderboards(await _localLeaderboardService.BuildAsync("maribor")),
                BinPhotoGallery = binPhotoGallery,
                WalksThisWeek = weeklyActivity.Count,
                KilometersThisWeek = weeklyActivity.Sum(walk => walk.DistanceMeters) / 1000,
                ActiveDogsThisWeek = weeklyActivity.Select(walk => walk.DogId).Distinct().Count()
            });
        }

        public IActionResult Terms()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UserProfile()
        {
            var user = await _userManager.Users
                .Include(u => u.TrashBins)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity!.Name);

            if (user == null)
            {
                return NotFound();
            }

            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var dogs = await _context.Dogs
                .Where(d => d.OwnerId == user.Id)
                .OrderBy(d => d.Name)
                .ToListAsync();

            var completedWalks = await _context.Walks
                .Include(w => w.PlannedWalk)
                    .ThenInclude(plan => plan!.Stops)
                .Where(w => w.OwnerId == user.Id && w.Status == "Completed")
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();
            var parkVisits = await _context.DogParkVisits
                .Where(visit => visit.UserId == user.Id)
                .ToListAsync();
            var stampCollection = _mapStampService.BuildCollection(parkVisits);

            var dogSummaries = dogs
                .Select(dog =>
                {
                    var dogWalks = completedWalks.Where(w => w.DogId == dog.Id).ToList();
                    return new ProfileDogSummary
                    {
                        Id = dog.Id,
                        Name = dog.Name,
                        PhotoUrl = dog.PhotoUrl,
                        DistanceKm = dogWalks.Sum(w => w.DistanceMeters) / 1000,
                        WalkCount = dogWalks.Count
                    };
                })
                .OrderByDescending(dog => dog.DistanceKm)
                .ToList();

            var totalBins = user.TrashBins?.Count ?? 0;
            var totalDistanceKm = completedWalks.Sum(w => w.DistanceMeters) / 1000;
            var totalWalkDuration = TimeSpan.FromTicks(completedWalks
                .Where(w => w.EndedAt.HasValue)
                .Sum(w => (w.EndedAt!.Value - w.StartedAt).Ticks));
            var gamificationProfile = await _gamificationService.EnsureProfileAsync(user.Id);
            var levelInfo = _gamificationService.CalculateLevelInfo(gamificationProfile.TotalXp);
            var streaks = await _gamificationService.GetStreaksAsync(user.Id);
            var walkStreak = streaks.Single(streak => streak.StreakType == GamificationStreakConstants.Walk);
            var founderBadges = await _context.FounderBadges
                .Where(badge => badge.UserId == user.Id)
                .OrderByDescending(badge => badge.UnlockedAt)
                .ToListAsync();
            var ownedAchievements = await _userAchievementService.GetOwnedAsync(user.Id);

            var model = new UserProfileViewModel
            {
                Email = user.Email ?? string.Empty,
                ProfileImageUrl = user.ProfileImageUrl,
                TotalBins = totalBins,
                Achievements = UserAchievementPresentationBuilder.Build(
                    new UserAchievementProgress(dogs.Count, completedWalks.Count, totalDistanceKm, totalBins, parkVisits.Select(visit => visit.PlaceKey).Distinct().Count()),
                    ownedAchievements),
                DisplayName = user.DisplayName,
                TotalDogs = dogs.Count,
                TotalWalks = completedWalks.Count,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                TotalDistanceKm = totalDistanceKm,
                TotalWalkDuration = totalWalkDuration,
                Dogs = dogSummaries,
                ActivityInsights = ActivityInsightsBuilder.Build(completedWalks),
                SeasonalMapTheme = _seasonalEventService.GetCurrentMapTheme(),
                SeasonalEvents = _seasonalEventService
                    .BuildProgress(completedWalks, parkVisits)
                    .Select(item => new SeasonalEventViewModel
                    {
                        Name = item.Name,
                        Description = item.Description,
                        RewardName = item.RewardName,
                        Theme = item.Theme,
                        EndsOn = item.EndsOn,
                        Current = item.Current,
                        Target = item.Target,
                        ProgressPercent = item.ProgressPercent,
                        IsComplete = item.IsComplete
                    })
                    .ToList(),
                MapStamps = new MapStampCollectionViewModel
                {
                    TotalStamps = stampCollection.TotalStamps,
                    CommonCount = stampCollection.CommonCount,
                    RareCount = stampCollection.RareCount,
                    EpicCount = stampCollection.EpicCount,
                    LegendaryCount = stampCollection.LegendaryCount,
                    Stamps = stampCollection.Stamps.Take(8).Select(stamp => new MapStampViewModel
                    {
                        Name = stamp.Name,
                        Area = stamp.Area,
                        Rarity = stamp.Rarity,
                        VisitCount = stamp.VisitCount,
                        LastCollectedAt = stamp.LastCollectedAt
                    }).ToList()
                },
                FounderBadges = founderBadges.Select(badge => new FounderBadgeViewModel
                {
                    AreaName = badge.AreaName,
                    BadgeType = badge.BadgeType,
                    UnlockedAt = badge.UnlockedAt
                }).ToList(),
                Gamification = new GamificationProfileViewModel
                {
                    TotalXp = levelInfo.TotalXp,
                    Level = levelInfo.Level,
                    Title = levelInfo.Title,
                    ProgressPercent = levelInfo.ProgressPercent,
                    XpIntoLevel = levelInfo.XpIntoLevel,
                    XpForNextLevel = levelInfo.XpForNextLevel,
                    XpRemaining = levelInfo.XpRemaining,
                    CurrentStreakDays = walkStreak.EffectiveCurrentDays,
                    LongestStreakDays = walkStreak.LongestDays,
                    AvatarFlameTier = GetStrongestFlameTier(streaks),
                    Streaks = streaks
                        .Where(streak => streak.StreakType is GamificationStreakConstants.Walk or GamificationStreakConstants.Explorer)
                        .Select(streak => new GamificationStreakViewModel
                    {
                        StreakType = streak.StreakType,
                        Label = streak.Label,
                        StoredCurrentDays = streak.StoredCurrentDays,
                        CurrentDays = streak.EffectiveCurrentDays,
                        LongestDays = streak.LongestDays,
                        FreezeCredits = streak.FreezeCredits,
                        FlameTier = streak.FlameTier,
                        State = streak.State,
                        IsSafeToday = streak.IsSafeToday,
                        IsAtRiskToday = streak.IsAtRiskToday,
                        Guidance = streak.StreakType == GamificationStreakConstants.Walk
                            ? streak.Guidance
                            : streak.State switch
                            {
                                GamificationStreakState.SafeToday => "Današnji raziskovalni niz je varen.",
                                GamificationStreakState.AtRiskToday => "Danes obišči park, da ohraniš niz.",
                                _ => "Začni nov niz z obiskom parka."
                            }
                    }).ToList()
                }
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string DisplayName, IFormFile? ProfileImage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            user.DisplayName = DisplayName;

            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                var imageUrl = await _cloudinaryService.UploadImageAsync(ProfileImage);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    user.ProfileImageUrl = imageUrl;
                }
            }

            await _userManager.UpdateAsync(user);
            return RedirectToAction(nameof(UserProfile));
        }

        private static LocalLeaderboardViewModel MapLocalLeaderboards(LocalLeaderboardBoard board)
        {
            return new LocalLeaderboardViewModel
            {
                CityKey = board.CityKey,
                CityName = board.CityName,
                MostDistance = MapLeaderboardEntries(board.MostDistance),
                MostDiscoveries = MapLeaderboardEntries(board.MostDiscoveries),
                MostHelpful = MapLeaderboardEntries(board.MostHelpful),
                BestPhotos = MapLeaderboardEntries(board.BestPhotos),
                TopDogsThisWeek = MapLeaderboardEntries(board.TopDogsThisWeek)
            };
        }

        private static IReadOnlyList<LocalLeaderboardEntryViewModel> MapLeaderboardEntries(IReadOnlyList<LocalLeaderboardEntry> entries)
        {
            return entries.Select(entry => new LocalLeaderboardEntryViewModel
            {
                Rank = entry.Rank,
                UserId = entry.UserId,
                Score = entry.Score,
                Label = entry.Label,
                SubLabel = entry.SubLabel,
                ImageUrl = entry.ImageUrl,
                ScoreText = entry.ScoreText,
                DogId = entry.DogId
            }).ToList();
        }

        private static string GetStrongestFlameTier(IEnumerable<GamificationStreakInfo> streaks)
        {
            return streaks
                .Select(streak => streak.FlameTier)
                .OrderByDescending(GetFlameTierRank)
                .FirstOrDefault() ?? "none";
        }

        private static int GetFlameTierRank(string tier)
        {
            return tier switch
            {
                "legendary" => 3,
                "glowing" => 2,
                "small" => 1,
                _ => 0
            };
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            return PublicUserPresentation.Name(user);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> TestEmail()
        {
            var testEmail = "admin@doggydrop.app";
            await _emailSender.SendEmailAsync(
                testEmail,
                "Testno sporocilo iz DoggyDrop",
                "To je testni email, poslan iz aplikacije DoggyDrop.");

            return Content($"Testni e-mail poslan na {testEmail}");
        }
    }
}
