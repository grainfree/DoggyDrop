using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminEmailController(ApplicationDbContext db, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(EmailDeliveryStatus? status, ActivityEmailType? type, int page = 1)
    {
        if (!ModelState.IsValid || page is < 1 or > 100000 || status.HasValue && !Enum.IsDefined(status.Value) || type.HasValue && !Enum.IsDefined(type.Value)) return BadRequest();
        var query = db.NotificationOutbox.AsNoTracking();
        if (status.HasValue) query = query.Where(n => n.Status == status);
        if (type.HasValue) query = query.Where(n => n.Type == type);
        var rows = await query.OrderByDescending(n => n.Id).Skip((page - 1) * 50).Take(51)
            .Select(n => new { n.Id, n.Type, n.Status, n.CreatedAt, n.SentAt, n.NextAttemptAt, n.AttemptCount, n.Failure, n.RecipientUser.Email }).ToListAsync();
        ViewBag.Page = page; ViewBag.More = rows.Count > 50; ViewBag.Status = status; ViewBag.Type = type;
        return View(rows.Take(50).Select(n => new EmailDeliveryRow(n.Id, n.Type, n.Status, n.CreatedAt, n.SentAt,
            n.NextAttemptAt, n.AttemptCount, n.Failure, Mask(n.Email))).ToList());
    }
    private static string Mask(string? email) => string.IsNullOrEmpty(email) ? "—" : email[..1] + "•••@•••";

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(4096)]
    public async Task<IActionResult> Retry(long id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest();
        var form = await Request.ReadFormAsync(ct);
        if (form.Keys.Any(k => k is not "id" and not "__RequestVerificationToken")) return BadRequest();
        if (await NotificationDelivery.RetryAsync(db, id, clock.GetUtcNow().UtcDateTime, ct) != 1) return Conflict();
        TempData["EmailRetry"] = "Obvestilo je ponovno v čakalni vrsti. Dostava še ni potrjena.";
        return RedirectToAction(nameof(Index));
    }
}
public sealed record EmailDeliveryRow(long Id, ActivityEmailType Type, EmailDeliveryStatus Status, DateTime CreatedAt,
    DateTime? SentAt, DateTime NextAttemptAt, int Attempts, EmailFailure Failure, string Recipient);
