using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminPlaceLogosController(BulkPlaceLogos logos, IDataProtectionProvider protection, TimeProvider clock) : Controller
{
    private readonly IDataProtector protector = protection.CreateProtector("DoggyDrop.BulkPlaceLogos.v1");

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384), RequestFormLimits(ValueCountLimit = 128)]
    public async Task<IActionResult> Preview(int[]? ids)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(owner)) return Forbid();
        if (!ModelState.IsValid) return BadRequest("Izbor ni veljaven.");
        try
        {
            var places = await logos.PreviewAsync(ids);
            var ticket = new BulkLogoTicket(places.Select(p => new PlaceLogoVersion(p.Id, p.UpdatedAtTicks)).ToArray(), owner, clock.GetUtcNow().AddMinutes(10));
            return View("Confirm", new BulkLogoPage(places, protector.Protect(JsonSerializer.Serialize(ticket))));
        }
        catch (BulkPlaceLogoException error) { TempData["DataMessage"] = error.Message; return RedirectToAction("Index", "AdminPlaces"); }
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(PlaceLogoUploadPolicy.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = PlaceLogoUploadPolicy.MaxBytes, ValueLengthLimit = 24000, ValueCountLimit = 128)]
    public async Task<IActionResult> Apply(string? token, IFormFile? file)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(token) || token.Length > 20000) return BadRequest("Potrditev ni veljavna. Ponovi izbor.");
        BulkLogoTicket? ticket;
        try { ticket = JsonSerializer.Deserialize<BulkLogoTicket>(protector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        { return BadRequest("Potrditev ni veljavna. Ponovi izbor."); }
        if (ticket == null || ticket.ExpiresAt <= clock.GetUtcNow() || string.IsNullOrEmpty(ticket.Owner) ||
            ticket.Owner != User.FindFirstValue(ClaimTypes.NameIdentifier)) return BadRequest("Potrditev je potekla ali ni tvoja. Ponovi izbor.");
        try
        {
            var count = await logos.ApplyAsync(ticket.Places, file);
            TempData["DataMessage"] = $"Logotip je nastavljen za {count} lokacij. Vse uporabljajo isti shranjeni logotip.";
        }
        catch (BulkPlaceLogoException error) { TempData["DataMessage"] = error.Message; }
        return RedirectToAction("Index", "AdminPlaces");
    }
}
