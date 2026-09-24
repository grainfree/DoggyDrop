using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

public sealed class PlacesController(ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var place = await context.Places.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.IsActive);
        return place == null ? NotFound() : View(PlaceDetailsViewModel.FromPlace(place));
    }
}
