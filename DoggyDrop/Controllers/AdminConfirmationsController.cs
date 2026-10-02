using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

public sealed record ConfirmationHistoryRow(DateTime CreatedAt, bool CurrentEvidence);
public sealed record ConfirmationHistory(string Kind, int Id, string Name, int Total, int Page, InfrastructureTrustSummary Summary,
    IReadOnlyList<ConfirmationHistoryRow> Rows);

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminConfirmationsController(ApplicationDbContext db, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string kind, int id, int page = 1, CancellationToken ct = default)
    {
        if (kind is not ("bin" or "water") || id <= 0 || page is < 1 or > 100000) return BadRequest();
        var water = kind == "water";
        var target = water ? await db.WaterPoints.AsNoTracking().Where(p => p.Id == id).Select(p => new { p.Name, p.EvidenceVersion }).SingleOrDefaultAsync(ct)
            : await db.TrashBins.AsNoTracking().Where(p => p.Id == id).Select(p => new { Name = (string?)p.Name, p.EvidenceVersion }).SingleOrDefaultAsync(ct);
        if (target == null) return NotFound();
        var query = db.InfrastructureConfirmations.AsNoTracking().Where(c => water ? c.WaterPointId == id : c.TrashBinId == id);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).Skip((page - 1) * 100).Take(100)
            .Select(c => new ConfirmationHistoryRow(c.CreatedAt, c.EvidenceVersion == target.EvidenceVersion)).ToListAsync(ct);
        var service = new InfrastructureTrust(db, clock);
        var trust = water ? await service.WaterAsync([id], ct) : await service.BinsAsync([id], ct);
        return View(new ConfirmationHistory(kind, id, target.Name ?? "Pitnik", total, page, trust.GetValueOrDefault(id, InfrastructureTrust.Empty), rows));
    }
}
