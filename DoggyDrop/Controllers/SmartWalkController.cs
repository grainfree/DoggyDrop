using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SmartWalkController(SmartWalkPlanner planner, SmartWalkPreviews previews, ApplicationDbContext db) : Controller
{
    public sealed record Input(WalkingRoutesController.CoordinateInput? Start, int Minutes, string? WalkType,
        string[]? Preferences, int Variant = 0, int? PlaceId = null);
    public sealed record Fact(string Kind, int Count, string Text);
    public sealed record Highlight(string Kind, string Name, double Latitude, double Longitude);
    public sealed record Result(string Token, double[][] Points, double DistanceMeters, double DurationSeconds,
        IReadOnlyList<Fact> Facts, IReadOnlyList<Highlight> Highlights, string Notice);
    [HttpPost("/api/smart-walk"), ValidateAntiForgeryToken, EnableRateLimiting("walking-route"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Generate([FromBody] Input? input, CancellationToken ct)
    {
        var start = input?.Start?.ToCoordinate();
        if (!ModelState.IsValid || start is not { IsValid: true } || input?.Preferences == null || input.Preferences.Length > 3
            || input.Preferences.Distinct().Count() != input.Preferences.Length || input.Preferences.Any(p => p is not ("water" or "bin" or "park"))) return BadRequest(new { error = "Preveri izhodišče in izbrane možnosti." });
        var request = new SmartWalkInput(start, input.Minutes, input.WalkType ?? "", input.Preferences.Contains("water"), input.Preferences.Contains("bin"), input.Preferences.Contains("park"), input.Variant, input.PlaceId);
        if (!request.IsValid) return BadRequest(new { error = "Preveri trajanje, vrsto sprehoda in cilj." });
        var owner = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (!previews.TryBegin(owner, out var lease)) { Response.Headers.RetryAfter = "60"; return StatusCode(429, new { error = "Počakaj trenutek pred novim predlogom." }); }
        using (lease)
        {
            SmartWalkSelection? selection;
            try { selection = await planner.GenerateAsync(request, ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { selection = null; }
            if (selection == null) return StatusCode(503, new { error = "Trenutno ne moremo pripraviti primerne pešpoti. Poskusi drugo izhodišče ali trajanje." });
            if (!await SmartWalkEligibility.PreviewAvailableAsync(db, selection.Stops, ct)) return Conflict(new { error = "Izbrani postanek ni več na voljo. Pripravi nov predlog." });
            // Provider work can take seconds. Re-read the bounded public dataset
            // before describing facts so intervening retirement is respected.
            selection = selection with { Facts = SmartWalkGeometry.Facts(selection.Route.Points, await planner.LoadNearbyAsync(request, ct)) };
            var preview = previews.Put(owner, selection);
            if (previews.Get(owner, preview.Token) == null) return StatusCode(503, new { error = "Predloga trenutno ni mogoče obdržati. Poskusi znova čez trenutek." });
            var facts = new List<Fact>();
            foreach (var kind in new[] { "water", "bin", "park" })
            {
                var count = selection.Facts.Count(p => p.Kind == kind); if (count == 0) continue;
                var text = kind switch
                {
                    "water" => (count % 100) switch { 1 => $"{count} pitnik ob poti", 2 => $"{count} pitnika ob poti", 3 or 4 => $"{count} pitniki ob poti", _ => $"{count} pitnikov ob poti" },
                    "bin" => (count % 100) switch { 1 => $"{count} koš ob poti", 2 => $"{count} koša ob poti", 3 or 4 => $"{count} koši ob poti", _ => $"{count} košev ob poti" },
                    _ => "Ob poti: " + selection.Facts.First(p => p.Kind == "park").Name
                };
                facts.Add(new(kind, count, text));
            }
            var highlights = selection.Stops.Concat(selection.Facts.Where(p => p.Kind != "bin").Take(4)).DistinctBy(p => (p.Kind, p.Id)).Take(5)
                .Select(p => new Highlight(p.Kind, p.Name, p.Latitude, p.Longitude)).ToArray();
            var deviation = Math.Abs(selection.Route.DurationSeconds!.Value / (request.Minutes * 60d) - 1);
            var explanation = deviation <= SmartWalkPolicy.DurationTolerance ? "Pot se približa izbranemu trajanju. " : "Predlog odstopa od izbranega trajanja; preveri prikazani čas. ";
            if (request.WalkType == "loop" && SmartWalkGeometry.Repetition(selection.Route.Points) > .25) explanation += "Del poti se ponovi. ";
            return Json(new Result(preview.Token, selection.Route.Points.Select(p => new[] { p.Latitude, p.Longitude }).ToArray(), selection.Route.DistanceMeters, selection.Route.DurationSeconds!.Value,
                facts, highlights, explanation + "Čas hoje je okviren. Upoštevaj označbe, dostop in razmere na terenu. Razpoložljivost pitnikov ni zagotovljena."));
        }
    }
    [HttpGet("/api/smart-walk/destinations"), EnableRateLimiting("walking-route")]
    public async Task<IActionResult> Destinations(string? q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length is < 2 or > 80) return BadRequest();
        var rows = await db.Places.AsNoTracking().ForPublicDetails().Where(p => p.Name.ToLower().Contains(q.Trim().ToLower()))
            .OrderBy(p => p.Name).ThenBy(p => p.Id).Take(20).Select(p => new { p.Id, p.Name, p.Category }).ToListAsync(ct);
        return Json(rows.Where(p => PublicPlaceEligibility.ValidName(p.Name)).Select(p => new { p.Id, p.Name, category = PlaceCategories.Label(p.Category) }));
    }
}
