using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BinContributionsController(ApplicationDbContext db, BinContributions contributions) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Create(int id, bool photo = false) => await Form(new()
        { BinId = id, Type = photo ? BinContributionType.Photo : BinContributionType.Issue });

    [HttpPost, ValidateAntiForgeryToken, BinSubmissionLimit, RequestSizeLimit(BinPhotoUploadPolicy.MaxBytes + 65536)]
    public async Task<IActionResult> Create(BinContributionInput input)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner == null) return Forbid();
        if (ModelState.IsValid)
        {
            try
            {
                await contributions.SubmitAsync(input.BinId, owner, input.RequestId, input.Type, input.Reason,
                    input.Description, input.Latitude, input.Longitude, input.DuplicateBinId, input.Photo);
                TempData["ContributionMessage"] = input.Type == BinContributionType.Photo ? "Fotografija je poslana v pregled." :
                    input.Reason == BinIssueReason.WRONG_LOCATION ? "Predlog nove lokacije je poslan v pregled." : "Hvala. Prijavo bomo pregledali.";
                return RedirectToAction(nameof(Mine));
            }
            catch (BinReviewException error) { ModelState.AddModelError("", error.Message); }
        }
        Response.StatusCode = 400;
        return await Form(input);
    }

    private async Task<IActionResult> Form(BinContributionInput input)
    {
        var bin = await db.TrashBins.AsNoTracking().PublicBins().Include(b => b.DataSource).SingleOrDefaultAsync(b => b.Id == input.BinId);
        if (bin == null) return NotFound();
        var nearby = await db.TrashBins.AsNoTracking().PublicBins().Where(b => b.Id != bin.Id &&
            b.Latitude >= bin.Latitude - .01 && b.Latitude <= bin.Latitude + .01 && b.Longitude >= bin.Longitude - .02 && b.Longitude <= bin.Longitude + .02)
            .Select(b => new { b.Id, b.Name, b.Latitude, b.Longitude }).ToListAsync();
        return View("Create", new BinContributionPage(input, bin.Id, bin.Name, bin.Latitude, bin.Longitude, bin.FullImageUrl, bin.DataSource?.Name,
            nearby.Select(b => new BinNeighbour(b.Id, b.Name, DuplicateCandidates.Distance(bin.Latitude, bin.Longitude, b.Latitude, b.Longitude)))
                .OrderBy(b => b.Metres).Take(30).ToList()));
    }

    [HttpGet]
    public async Task<IActionResult> Mine(int page = 1)
    {
        if (page is < 1 or > 100000) return BadRequest();
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner == null) return Forbid();
        // No pending image URL or private Admin note in user history.
        var rows = await db.BinContributions.AsNoTracking().Where(c => c.SubmittedByUserId == owner)
            .OrderByDescending(c => c.Id).Skip((page - 1) * 100).Take(101).Select(c => new BinContributionHistory(c.Id, c.BinId, c.Type, c.Reason, c.Status, c.CreatedAt,
                c.Bin.Name, c.Bin.IsApproved && !c.Bin.IsRetired)).ToListAsync();
        ViewBag.Page = page; ViewBag.HasNext = rows.Count > 100;
        return View(rows.Take(100).ToList());
    }
}
public sealed record BinContributionHistory(long Id, int BinId, BinContributionType Type, BinIssueReason? Reason, BinContributionStatus Status, DateTime CreatedAt,
    string? BinName, bool CanShowOnMap);
