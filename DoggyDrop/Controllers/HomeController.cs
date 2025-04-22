using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICloudinaryService _cloudinaryService;

        public HomeController(
            ILogger<HomeController> logger,
            UserManager<ApplicationUser> userManager,
            ICloudinaryService cloudinaryService)
        {
            _logger = logger;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> UserProfile()
        {
            var user = await _userManager.Users
                .Include(u => u.TrashBins)
                .FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);

            if (user == null) return NotFound();

            var model = new UserProfileViewModel
            {
                Email = user.Email,
                ProfileImageUrl = user.ProfileImageUrl,
                TotalBins = user.TrashBins?.Count ?? 0,
                Badges = GetBadges(user.TrashBins?.Count ?? 0),
                DisplayName = user.DisplayName
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string DisplayName, IFormFile ProfileImage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

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
            return RedirectToAction("UserProfile");
        }

        private List<string> GetBadges(int totalBins)
        {
            var badges = new List<string>();
            if (totalBins >= 1) badges.Add("🐾 Prvi koš");
            if (totalBins >= 5) badges.Add("🌟 Aktivni predlagatelj");
            if (totalBins >= 10) badges.Add("🏆 Zbiratelj lokacij");
            return badges;
        }
    }
}
