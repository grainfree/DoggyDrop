using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SavedPlacesController(ApplicationDbContext context, PlaceLogoCloudName logoCloud) : Controller
{
    private string? CurrentUserId => User.Identity?.IsAuthenticated == true
        ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var places = await context.SavedPlaces.AsNoTracking()
            .Where(saved => saved.UserId == userId && saved.Place.IsActive &&
                PlaceCategories.Supported.Contains(saved.Place.Category))
            .OrderByDescending(saved => saved.SavedAt).ThenByDescending(saved => saved.PlaceId)
            .Select(saved => new { saved.Place.Id, saved.Place.Name, saved.Place.Category,
                saved.Place.Address, saved.Place.Latitude, saved.Place.Longitude, saved.Place.LogoUrl })
            .ToListAsync();
        return View(new PlaceDiscoveryViewModel(places.Select(place =>
        {
            var category = PlaceCategories.Get(place.Category);
            return new PlaceDiscoveryItem(place.Id, place.Name, place.Category,
                category.Label, category.Key, category.IconClass, category.IsCommercial,
                place.Address, place.Latitude, place.Longitude,
                PlaceCategories.PublicLogo(place.Category, place.LogoUrl, logoCloud.Value));
        }).ToList()) { SavedPlaceIds = places.Select(place => place.Id).ToHashSet() });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int placeId, string? returnUrl)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        var category = await context.Places.Where(place => place.Id == placeId && place.IsActive &&
                PlaceCategories.Supported.Contains(place.Category))
            .Select(place => (int?)place.Category).SingleOrDefaultAsync();
        if (category == null) return NotFound();

        // PostgreSQL and SQLite support this atomic, parameterized insert. A racing save
        // is a no-op, without a duplicate-key exception or changing the original SavedAt.
        // Recheck the Place at insertion time in case it was deactivated/deleted meanwhile.
        var inserted = await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SavedPlaces" ("UserId", "PlaceId", "SavedAt")
            SELECT {userId}, "Id", {DateTime.UtcNow} FROM "Places"
            WHERE "Id" = {placeId} AND "IsActive" AND "Category" = {category.Value}
            ON CONFLICT ("UserId", "PlaceId") DO NOTHING
            """);
        if (inserted == 0 && !await context.SavedPlaces.AnyAsync(saved => saved.UserId == userId && saved.PlaceId == placeId))
            return NotFound();
        TempData["SavedPlaceMessage"] = "Lokacija je shranjena.";
        return ReturnToPage(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int placeId, string? returnUrl)
    {
        var userId = CurrentUserId;
        if (string.IsNullOrEmpty(userId)) return Challenge();
        await context.SavedPlaces.Where(saved => saved.UserId == userId && saved.PlaceId == placeId)
            .ExecuteDeleteAsync();
        TempData["SavedPlaceMessage"] = "Lokacija je odstranjena iz shranjenih.";
        return ReturnToPage(returnUrl);
    }

    private IActionResult ReturnToPage(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
}
