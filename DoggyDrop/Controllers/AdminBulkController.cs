using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminBulkController(ApplicationDbContext db, IDataProtectionProvider protection) : Controller
{
    private readonly IDataProtector protector = protection.CreateProtector("DoggyDrop.AdminBulk.v1");

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Preview(BulkInput input)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Izbor ni veljaven.");
        var preview = await new AdminBulkTools(db).PreviewAsync(input);
        if (preview == null) return BadRequest("Izberi od 1 do 100 veljavnih zapisov, dejanje in po potrebi vir. Vir izberi le za dodelitev.");
        var token = protector.Protect(JsonSerializer.Serialize(new BulkTicket(preview.Input, userId, DateTime.UtcNow.AddMinutes(10))));
        return View("Confirm", preview with { Token = token });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Apply(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 14000) return BadRequest("Potrditev ni veljavna.");
        BulkTicket? ticket;
        try { ticket = JsonSerializer.Deserialize<BulkTicket>(protector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        { return BadRequest("Potrditev ni veljavna. Ponovi izbor."); }
        if (ticket == null || ticket.Input == null || ticket.ExpiresAt < DateTime.UtcNow ||
            ticket.UserId != User.FindFirstValue(ClaimTypes.NameIdentifier)) return BadRequest("Potrditev je potekla. Ponovi izbor.");
        try
        {
            var count = await new AdminBulkTools(db).ApplyAsync(ticket.Input);
            TempData["DataMessage"] = $"Posodobljenih {count} zapisov.";
        }
        catch (InvalidOperationException)
        { TempData["DataMessage"] = "Izbor ali vir se je spremenil. Nič ni bilo shranjeno. Ponovi izbor."; }
        catch (DbUpdateException)
        { TempData["DataMessage"] = "Podatki so bili med urejanjem spremenjeni ali jih ni bilo mogoče shraniti. Nič ni bilo shranjeno. Osveži in ponovi izbor."; }
        return RedirectToAction("Index", ticket.Input.Target == BulkTarget.Places ? "AdminPlaces" : "AdminBins");
    }
}
