using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminBinContributionsController(ApplicationDbContext db, BinContributions contributions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(bool history = false, int page = 1)
    {
        if (page is < 1 or > 100000) return BadRequest();
        ViewBag.History = history;
        ViewBag.NewBins = await db.TrashBins.AsNoTracking().Where(b => !b.IsApproved && !b.IsRetired && !b.IsRejected)
            .OrderBy(b => b.DateAdded).Take(100).ToListAsync();
        var rows = await db.BinContributions.AsNoTracking().Include(c => c.Bin).ThenInclude(b => b.DataSource)
            .Where(c => history || c.Status == BinContributionStatus.Pending).OrderByDescending(c => c.Id).Skip((page - 1) * 100).Take(101).ToListAsync();
        ViewBag.Page = page; ViewBag.HasNext = rows.Count > 100;
        return View(rows.Take(100).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Review(long id)
    {
        var item = await db.BinContributions.AsNoTracking().Include(c => c.Bin).ThenInclude(b => b.DataSource).SingleOrDefaultAsync(c => c.Id == id);
        if (item == null) return NotFound();
        ViewBag.Duplicate = item.PossibleDuplicateBinId.HasValue ? await db.TrashBins.AsNoTracking().Include(b => b.DataSource)
            .SingleOrDefaultAsync(b => b.Id == item.PossibleDuplicateBinId) : null;
        return View(item);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Review(long id, [BindRequired] bool accept, bool retire, string? note)
    {
        var reviewer = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (reviewer == null) return Forbid();
        if (!ModelState.IsValid) return BadRequest();
        try { await contributions.ReviewAsync(id, reviewer, accept, retire, note); }
        catch (BinReviewException error) { return Conflict(error.Message); }
        TempData["ContributionMessage"] = "Odločitev je shranjena.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Lifecycle(int id)
    {
        var bin = await db.TrashBins.AsNoTracking().Include(b => b.DataSource).SingleOrDefaultAsync(b => b.Id == id);
        return bin == null ? NotFound() : View(bin);
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Lifecycle(int id, string snapshot, [BindRequired] bool retired)
    {
        if (!ModelState.IsValid) return BadRequest();
        try { await contributions.LifecycleAsync(id, snapshot, retired); }
        catch (BinReviewException error) { return Conflict(error.Message); }
        return RedirectToAction("Index", "AdminBins");
    }
}
