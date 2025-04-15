using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DoggyDrop.Data;
using DoggyDrop.Services;
using CloudinaryDotNet.Actions;
using CloudinaryDotNet;

namespace DoggyDrop.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private readonly CloudinaryService _cloudinaryService;

    public HomeController(
        ILogger<HomeController> logger,
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment environment,
        CloudinaryService cloudinaryService)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
        _environment = environment;
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

        var user = await _userManager.FindByIdAsync(userId);
        var profileImageUrl = user?.ProfileImageUrl;

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
    public async Task<IActionResult> UploadProfileImage(IFormFile profileImage, [FromServices] Cloudinary cloudinary)
    {
        if (profileImage != null && profileImage.Length > 0)
        {
            try
            {
                var userId = _userManager.GetUserId(User);

                await using var stream = profileImage.OpenReadStream();

                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(profileImage.FileName, stream),
                    PublicId = $"profile_pictures/{userId}",
                    Overwrite = true
                };

                var uploadResult = await cloudinary.UploadAsync(uploadParams);

                if (uploadResult.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    var user = await _userManager.GetUserAsync(User);
                    user.ProfileImageUrl = uploadResult.SecureUrl.ToString();
                    await _userManager.UpdateAsync(user);

                    TempData["SuccessMessage"] = "Profilna slika uspešno posodobljena.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Napaka pri nalaganju: {uploadResult.Error?.Message}";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Izjema: {ex.Message}";
            }
        }
        else
        {
            TempData["ErrorMessage"] = "Prosimo, izberi datoteko za nalaganje.";
        }

        return RedirectToAction("UserProfile");
    }


}
