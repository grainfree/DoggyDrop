using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace DoggyDrop.Controllers;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlacesController(ApplicationDbContext context, PlaceLogoCloudName logoCloud) : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Index()
    {
        var places = await context.Places.AsNoTracking()
            .Where(place => place.IsActive && PlaceCategories.Supported.Contains(place.Category))
            .OrderBy(place => place.Name).ThenBy(place => place.Id)
            .Select(place => new { place.Id, place.Name, place.Category, place.Address,
                place.Latitude, place.Longitude, place.LogoUrl })
            .ToListAsync();
        var userId = User?.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var savedIds = string.IsNullOrEmpty(userId) ? new HashSet<int>() :
            (await context.SavedPlaces.AsNoTracking().Where(saved => saved.UserId == userId)
                .Select(saved => saved.PlaceId).ToListAsync()).ToHashSet();
        return View(new PlaceDiscoveryViewModel(places.Select(place =>
        {
            var category = PlaceCategories.Get(place.Category);
            return new PlaceDiscoveryItem(place.Id, place.Name, place.Category,
                category.Label, category.Key, category.IconClass, category.IsCommercial,
                place.Address, place.Latitude, place.Longitude,
                PlaceCategories.PublicLogo(place.Category, place.LogoUrl, logoCloud.Value));
        }).ToList()) { SavedPlaceIds = savedIds });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var place = await context.Places.AsNoTracking()
            .Where(item => item.Id == id && item.IsActive && PlaceCategories.Supported.Contains(item.Category))
            .Select(item => new PlaceDetailsData(item.Id, item.Name, item.Category, item.Latitude, item.Longitude,
                item.Address, item.Phone, item.WebsiteUrl, item.OpeningHours, item.Description, item.ImageUrl, item.LogoUrl,
                item.Amenities.Select(amenity => amenity.AmenityType).ToList()))
            .SingleOrDefaultAsync();
        if (place == null) return NotFound();
        var userId = User?.Identity?.IsAuthenticated == true ? User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        var isSaved = !string.IsNullOrEmpty(userId) && await context.SavedPlaces.AsNoTracking()
            .AnyAsync(saved => saved.UserId == userId && saved.PlaceId == id);
        return View(PlaceDetailsViewModel.FromPublicData(place, logoCloud.Value) with { IsSaved = isSaved });
    }
}
