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

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context, UserManager<IdentityUser> userManager)
    {
        _logger = logger;
        _context = context;
        _userManager = userManager;
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

        var viewModel = new UserProfileViewModel
        {
            Email = userEmail,
            TotalBins = totalBins,
            Badges = badges
        };

        return View("UserProfile", viewModel);
    }
}
