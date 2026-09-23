using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DoggyDrop.Controllers;

[Authorize]
public sealed class WeeklyGoalsController : Controller
{
    private readonly IWeeklyGoalsService _goals;

    public WeeklyGoalsController(IWeeklyGoalsService goals) => _goals = goals;

    public async Task<IActionResult> Index()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        return View(await _goals.GetForUserAsync(userId));
    }
}
