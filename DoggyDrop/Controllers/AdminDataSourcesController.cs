using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDataSourcesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index() => View(await db.DataSources.AsNoTracking().OrderBy(s => s.Name).ThenBy(s => s.Id)
        .Select(s => new DataSourceRow(s.Id, s.Name, s.Type, s.DataDate, s.UpdatedAt,
            db.TrashBins.Count(b => b.DataSourceId == s.Id), db.Places.Count(p => p.DataSourceId == s.Id))).ToListAsync());

    [HttpGet]
    public IActionResult Create() => View("Edit", new DataSourceInput());

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var source = await db.DataSources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (source == null) return NotFound();
        ViewBag.SourceId = id;
        return View(DataSourceInput.From(source));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Create(DataSourceInput input)
    {
        if (!ModelState.IsValid) return View("Edit", input);
        var source = new DataSource(); input.ApplyTo(source); db.DataSources.Add(source);
        await db.SaveChangesAsync();
        TempData["DataMessage"] = "Vir podatkov je dodan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Edit(int id, DataSourceInput input)
    {
        var source = await db.DataSources.SingleOrDefaultAsync(s => s.Id == id);
        if (source == null) return NotFound();
        ViewBag.SourceId = id;
        if (!ModelState.IsValid) return View(input);
        input.ApplyTo(source); await db.SaveChangesAsync();
        TempData["DataMessage"] = "Vir podatkov je posodobljen.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var source = await db.DataSources.AsNoTracking().Where(s => s.Id == id)
            .Select(s => new DataSourceRow(s.Id, s.Name, s.Type, s.DataDate, s.UpdatedAt,
                db.TrashBins.Count(b => b.DataSourceId == id), db.Places.Count(p => p.DataSourceId == id))).SingleOrDefaultAsync();
        return source == null ? NotFound() : View(source);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var source = await db.DataSources.SingleOrDefaultAsync(s => s.Id == id);
        if (source == null) return NotFound();
        db.DataSources.Remove(source); await db.SaveChangesAsync();
        TempData["DataMessage"] = "Vir je odstranjen. Koši in lokacije so ohranjeni brez povezave z virom.";
        return RedirectToAction(nameof(Index));
    }
}
