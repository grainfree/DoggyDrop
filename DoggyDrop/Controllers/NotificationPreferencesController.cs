using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationPreferencesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner == null) return Forbid();
        return View(!await db.NotificationPreferences.AnyAsync(p => p.UserId == owner && !p.ContributionUpdates));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4096)]
    public async Task<IActionResult> Index(bool? contributionUpdates, CancellationToken ct)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner == null) return Forbid();
        var form = await Request.ReadFormAsync(ct);
        if (!ModelState.IsValid || contributionUpdates == null || form.Keys.Any(k => k is not "contributionUpdates" and not "__RequestVerificationToken")) return BadRequest();
        // Atomic upsert, including simultaneous first preference changes; current owner only.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NotificationPreferences" ("UserId", "ContributionUpdates") VALUES ({owner}, {contributionUpdates.Value})
            ON CONFLICT ("UserId") DO UPDATE SET "ContributionUpdates" = EXCLUDED."ContributionUpdates"
            """, ct);
        TempData["EmailPreferenceSaved"] = "Nastavitve e-poštnih obvestil so shranjene.";
        return RedirectToAction(nameof(Index));
    }
}
