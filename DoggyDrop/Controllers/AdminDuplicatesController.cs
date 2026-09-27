using DoggyDrop.Data;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDuplicatesController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(BulkTarget target = BulkTarget.Bins, double? latitude = null, double? longitude = null, int radius = 1000)
    {
        if (!Enum.IsDefined(target) || !ModelState.IsValid) return BadRequest();
        if (latitude == null && longitude == null) return View(new DuplicatePage(target, null, null, radius, null));
        if (latitude == null || longitude == null || !double.IsFinite(latitude.Value) || !double.IsFinite(longitude.Value) ||
            latitude is < -90 or > 90 || longitude is < -180 or > 180 || radius is < 100 or > 5000)
            return BadRequest("Izberi veljavno središče in polmer od 100 do 5000 metrov.");
        var result = await new DuplicateCandidates(db).FindAsync(target, latitude.Value, longitude.Value, radius);
        return View(new DuplicatePage(target, latitude, longitude, radius, result));
    }
}
