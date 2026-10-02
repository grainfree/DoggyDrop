using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DoggyDrop.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConfirmationsController(ApplicationDbContext db, TimeProvider clock) : Controller
{
    [HttpPost("/api/confirmations/{kind}/{id:int}"), ValidateAntiForgeryToken,
     EnableRateLimiting("infrastructure-confirmation"), RequestSizeLimit(2048), Consumes("application/json")]
    public async Task<IActionResult> Create(string kind, int id, [FromBody] ConfirmationLocation? input, CancellationToken ct)
    {
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (owner == null) return Forbid();
        if (!ModelState.IsValid || kind is not ("bin" or "water") || !InfrastructureConfirmations.Valid(input))
            return BadRequest(new { outcome = "invalid" });
        var result = await new InfrastructureConfirmations(db, clock).ConfirmAsync(kind == "water", id, owner, input, ct);
        return result.Outcome switch { "accepted" or "cooldown" => Json(result),
            "unavailable" => StatusCode(410, result), _ => BadRequest(result) };
    }
}
