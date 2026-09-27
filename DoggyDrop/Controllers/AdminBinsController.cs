using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminBinsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? state, int? sourceId, bool noSource = false, int page = 1)
    {
        if (page is < 1 or > 100000 || sourceId is <= 0) return BadRequest();
        var query = db.TrashBins.AsNoTracking();
        if (state == "approved") query = query.Where(b => b.IsApproved);
        if (state == "pending") query = query.Where(b => !b.IsApproved);
        if (noSource) query = query.Where(b => b.DataSourceId == null);
        else if (sourceId.HasValue) query = query.Where(b => b.DataSourceId == sourceId);
        var rows = await query.OrderBy(b => b.Id).Skip((page - 1) * 100).Take(101)
            .Select(b => new AdminBinRow(b.Id, b.Name, b.IsApproved, b.DataSource == null ? null : b.DataSource.Name)).ToListAsync();
        ViewBag.Page = page; ViewBag.HasNext = rows.Count > 100; ViewBag.State = state;
        ViewBag.SourceId = sourceId; ViewBag.NoSource = noSource;
        ViewBag.DataSources = await db.DataSources.AsNoTracking().OrderBy(s => s.Name).Select(s => new SourceOption(s.Id, s.Name)).ToListAsync();
        return View(rows.Take(100).ToList());
    }
}
