using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

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
        return View(new PlaceDiscoveryViewModel(places.Select(place =>
        {
            var category = PlaceCategories.Get(place.Category);
            return new PlaceDiscoveryItem(place.Id, place.Name, place.Category,
                category.Label, category.Key, category.IconClass, category.IsCommercial,
                place.Address, place.Latitude, place.Longitude,
                PlaceCategories.PublicLogo(place.Category, place.LogoUrl, logoCloud.Value));
        }).ToList()));
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Details(int id)
    {
        var place = await context.Places.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.IsActive && PlaceCategories.Supported.Contains(item.Category));
        return place == null ? NotFound() : View(PlaceDetailsViewModel.FromPlace(place, logoCloud.Value));
    }
}
