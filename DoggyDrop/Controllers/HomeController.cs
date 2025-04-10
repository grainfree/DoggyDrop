using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DoggyDrop.Data;

namespace DoggyDrop.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IWebHostEnvironment _environment;


    public HomeController(
    ILogger<HomeController> logger,
    ApplicationDbContext context,
    UserManager<IdentityUser> userManager,
    IWebHostEnvironment environment)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
        _environment = environment;
    }


    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public async Task<IActionResult> UserProfile()
    {
        if (!User.Identity.IsAuthenticated)
            return RedirectToAction("Login", "Account", new { area = "Identity" });

        var userId = _userManager.GetUserId(User);
        var userEmail = User.Identity?.Name ?? "";

        var totalBins = await _context.TrashBins
            .Where(b => b.UserId == userId)
            .CountAsync();

        var badges = new List<string>();
        if (totalBins >= 1)
            badges.Add("🥇 Prvi koš");
        if (totalBins >= 5)
            badges.Add("🎯 Skupinski cilj");

        // 🖼️ pridobi profilno sliko
        var user = await _userManager.FindByIdAsync(userId);
        var profileImageUrl = (user as ApplicationUser)?.ProfileImageUrl;

        var viewModel = new UserProfileViewModel
        {
            Email = userEmail,
            TotalBins = totalBins,
            Badges = badges,
            ProfileImageUrl = profileImageUrl
        };

        return View("UserProfile", viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> UploadProfileImage(IFormFile profileImage)
    {
        if (profileImage != null && profileImage.Length > 0)
        {
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "profile-pics");
            Directory.CreateDirectory(uploadsFolder);

            var userId = _userManager.GetUserId(User);
            var uniqueFileName = $"{userId}{Path.GetExtension(profileImage.FileName)}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await profileImage.CopyToAsync(stream);
            }

            var imageUrl = "/profile-pics/" + uniqueFileName;

            // ➕ Shrani pot v bazo (ali v ViewModel preko storitve, odvisno od tvoje logike)

            // Za demo: lahko dodaš zapis v session (ali shraniš drugje)
            HttpContext.Session.SetString("ProfileImageUrl", imageUrl);
        }

        return RedirectToAction("UserProfile");
    }

}
