using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

        public HomeController(
            ILogger<HomeController> logger,
            UserManager<ApplicationUser> userManager,
            ICloudinaryService cloudinaryService,
            IEmailSender emailSender,
            ApplicationDbContext context,
            IGamificationService gamificationService)
        {
            _logger = logger;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
            _emailSender = emailSender;
            _context = context;
            _gamificationService = gamificationService;
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
        public IActionResult Settings()
        {
            return View();
        }

        [Authorize]
        public async Task<IActionResult> Community()
        {
            var userId = _userManager.GetUserId(User);
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);
            var acceptedFriendIds = string.IsNullOrWhiteSpace(userId)
                ? new List<string>()
                : await _context.Friendships
                    .Where(friendship =>
                        friendship.Status == "Accepted" &&
                        (friendship.RequesterId == userId || friendship.AddresseeId == userId))
                    .Select(friendship => friendship.RequesterId == userId ? friendship.AddresseeId : friendship.RequesterId)
                    .Distinct()
                    .ToListAsync();
            var recentWalks = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.Owner)
                .Include(w => w.Reactions)
                .Include(w => w.Comments)
                .Include(w => w.Photos)
                    .ThenInclude(photo => photo.Reactions)
                .Include(w => w.Photos)
                    .ThenInclude(photo => photo.PlannedWalkStop)
                .Where(w => w.Status == "Completed")
                .OrderByDescending(w => w.StartedAt)
                .Take(20)
                .ToListAsync();

            var friendWalks = acceptedFriendIds.Count == 0
                ? new List<Walk>()
                : await _context.Walks
                    .Include(w => w.Dog)
                    .Include(w => w.Owner)
                    .Include(w => w.Reactions)
                    .Include(w => w.Comments)
                    .Include(w => w.Photos)
                    .Where(w => w.Status == "Completed" && acceptedFriendIds.Contains(w.OwnerId))
                    .OrderByDescending(w => w.StartedAt)
                    .Take(10)
                    .ToListAsync();

            var photoFeed = recentWalks
                .SelectMany(walk => (walk.Photos ?? [])
                    .OrderByDescending(photo => photo.CreatedAt)
                    .Take(3)
                    .Select(photo => new CommunityPhotoFeedItem
                    {
                        WalkId = walk.Id,
                        PhotoId = photo.Id,
                        ImageUrl = photo.ImageUrl,
                        Caption = photo.Caption,
                        DogName = walk.Dog?.Name ?? "Pes",
                        DogPhotoUrl = walk.Dog?.PhotoUrl,
                        OwnerName = GetDisplayName(walk.Owner),
                        CreatedAt = photo.CreatedAt
                        ,
                        ReactionCount = photo.Reactions?.Count ?? 0,
                        IsReactedByCurrentUser = !string.IsNullOrWhiteSpace(userId)
                            && (photo.Reactions?.Any(reaction => reaction.UserId == userId) ?? false),
                        StopName = photo.PlannedWalkStop != null ? photo.PlannedWalkStop.Name : null
                    }))
                .OrderByDescending(item => item.CreatedAt)
                .Take(12)
                .ToList();

            var binPhotoGallery = (await _context.TrashBins
                .Include(bin => bin.User)
                .Where(bin => bin.IsApproved && !string.IsNullOrWhiteSpace(bin.ImageUrl))
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
                .ToList();

            var weeklyWalks = await _context.Walks
                .Include(w => w.Dog)
                .Include(w => w.Owner)
                .Where(w => w.Status == "Completed" && w.StartedAt >= weekStart)
                .ToListAsync();

            var visibleWeeklyWalks = weeklyWalks.Count > 0
                ? weeklyWalks
                : recentWalks
                .Where(w => w.StartedAt >= weekStart)
                .ToList();

            var model = new CommunityViewModel
            {
                RecentWalks = recentWalks.Take(10).Select(w => new CommunityWalkItem
                {
                    WalkId = w.Id,
                    DogName = w.Dog?.Name ?? "Pes",
                    DogPhotoUrl = w.Dog?.PhotoUrl,
                    OwnerName = GetDisplayName(w.Owner),
                    StartedAt = w.StartedAt,
                    DistanceKm = w.DistanceMeters / 1000,
                    UsedBinsCount = w.UsedBinsCount,
                    LikeCount = w.Reactions?.Count ?? 0,
                    CommentCount = w.Comments?.Count(comment => !comment.IsDeleted) ?? 0,
                    CoverPhotoUrl = w.Photos?
                        .OrderByDescending(photo => photo.CreatedAt)
                        .Select(photo => photo.ImageUrl)
                        .FirstOrDefault(),
                    PhotoCount = w.Photos?.Count ?? 0,
                    IsLikedByCurrentUser = !string.IsNullOrWhiteSpace(userId)
                        && (w.Reactions?.Any(reaction => reaction.UserId == userId) ?? false)
                }).ToList(),
                FriendsWalks = friendWalks.Select(w => new CommunityWalkItem
                {
                    WalkId = w.Id,
                    DogName = w.Dog?.Name ?? "Pes",
                    DogPhotoUrl = w.Dog?.PhotoUrl,
                    OwnerName = GetDisplayName(w.Owner),
                    StartedAt = w.StartedAt,
                    DistanceKm = w.DistanceMeters / 1000,
                    UsedBinsCount = w.UsedBinsCount,
                    LikeCount = w.Reactions?.Count ?? 0,
                    CommentCount = w.Comments?.Count(comment => !comment.IsDeleted) ?? 0,
                    CoverPhotoUrl = w.Photos?
                        .OrderByDescending(photo => photo.CreatedAt)
                        .Select(photo => photo.ImageUrl)
                        .FirstOrDefault(),
                    PhotoCount = w.Photos?.Count ?? 0,
                    IsLikedByCurrentUser = !string.IsNullOrWhiteSpace(userId)
                        && (w.Reactions?.Any(reaction => reaction.UserId == userId) ?? false)
                }).ToList(),
                WeeklyLeaders = visibleWeeklyWalks
                    .GroupBy(w => new
                    {
                        w.DogId,
                        DogName = w.Dog?.Name ?? "Pes",
                        DogPhotoUrl = w.Dog?.PhotoUrl,
                        OwnerName = GetDisplayName(w.Owner)
                    })
                    .Select(group => new CommunityLeaderboardItem
                    {
                        DogId = group.Key.DogId,
                        DogName = group.Key.DogName,
                        DogPhotoUrl = group.Key.DogPhotoUrl,
                        OwnerName = group.Key.OwnerName,
                        WeeklyDistanceKm = group.Sum(w => w.DistanceMeters) / 1000,
                        WalkCount = group.Count()
                    })
                    .OrderByDescending(item => item.WeeklyDistanceKm)
                    .Take(5)
                    .ToList(),
                PhotoFeed = photoFeed,
                BinPhotoGallery = binPhotoGallery,
                WalksThisWeek = visibleWeeklyWalks.Count,
                KilometersThisWeek = visibleWeeklyWalks.Sum(w => w.DistanceMeters) / 1000,
                ActiveDogsThisWeek = visibleWeeklyWalks.Select(w => w.DogId).Distinct().Count()
            };

            return View(model);
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
                .Where(w => w.OwnerId == user.Id && w.Status == "Completed")
                .OrderByDescending(w => w.StartedAt)
                .ToListAsync();

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

            var model = new UserProfileViewModel
            {
                Email = user.Email ?? string.Empty,
                ProfileImageUrl = user.ProfileImageUrl,
                TotalBins = totalBins,
                Badges = GetBadges(totalBins, completedWalks.Count, totalDistanceKm, dogs.Count),
                Achievements = GetAchievements(totalBins, completedWalks.Count, totalDistanceKm, dogs.Count),
                DisplayName = user.DisplayName,
                TotalDogs = dogs.Count,
                TotalWalks = completedWalks.Count,
                WalksThisWeek = completedWalks.Count(w => w.StartedAt >= weekStart),
                TotalDistanceKm = totalDistanceKm,
                TotalWalkDuration = totalWalkDuration,
                Dogs = dogSummaries,
                ActivityInsights = ActivityInsightsBuilder.Build(completedWalks),
                Gamification = new GamificationProfileViewModel
                {
                    TotalXp = levelInfo.TotalXp,
                    Level = levelInfo.Level,
                    Title = levelInfo.Title,
                    ProgressPercent = levelInfo.ProgressPercent,
                    XpIntoLevel = levelInfo.XpIntoLevel,
                    XpForNextLevel = levelInfo.XpForNextLevel,
                    XpRemaining = levelInfo.XpRemaining,
                    CurrentStreakDays = gamificationProfile.CurrentStreakDays,
                    LongestStreakDays = gamificationProfile.LongestStreakDays,
                    AvatarFlameTier = GetStrongestFlameTier(streaks),
                    Streaks = streaks.Select(streak => new GamificationStreakViewModel
                    {
                        StreakType = streak.StreakType,
                        Label = streak.Label,
                        CurrentDays = streak.CurrentDays,
                        LongestDays = streak.LongestDays,
                        FreezeCredits = streak.FreezeCredits,
                        FlameTier = streak.FlameTier
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

        private static List<string> GetBadges(int totalBins, int totalWalks, double totalDistanceKm, int totalDogs)
        {
            var badges = new List<string>();
            if (totalDogs >= 1) badges.Add("Dog parent");
            if (totalWalks >= 1) badges.Add("First walk");
            if (totalDistanceKm >= 10) badges.Add("10 km club");
            if (totalDistanceKm >= 100) badges.Add("100 km legend");
            if (totalBins >= 1) badges.Add("Bin helper");
            if (totalBins >= 10) badges.Add("Bin hero");
            return badges;
        }

        private static IReadOnlyList<AchievementItem> GetAchievements(int totalBins, int totalWalks, double totalDistanceKm, int totalDogs)
        {
            return
            [
                BuildAchievement("Dog parent", "Dodaj prvega psa v profil.", totalDogs, 1, suffix: "psov"),
                BuildAchievement("First walk", "Zakljuci prvi sprehod.", totalWalks, 1, suffix: "sprehodov"),
                BuildAchievement("City walker", "Prehodi 10 km skupaj.", totalDistanceKm, 10, suffix: "km"),
                BuildAchievement("Trail master", "Prehodi 100 km skupaj.", totalDistanceKm, 100, suffix: "km"),
                BuildAchievement("Bin helper", "Dodaj prvi pasji kos.", totalBins, 1, suffix: "kosev"),
                BuildAchievement("Bin hero", "Dodaj 10 pasjih kosev.", totalBins, 10, suffix: "kosev")
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
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? "DoggyDrop uporabnik";
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
