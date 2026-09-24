using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminPlacesController(ApplicationDbContext context) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? state, PlaceCategory? category)
    {
        var query = context.Places.AsNoTracking();
        if (state == "active") query = query.Where(place => place.IsActive);
        if (state == "inactive") query = query.Where(place => !place.IsActive);
        if (category is { } selected && Enum.IsDefined(selected))
            query = query.Where(place => place.Category == selected);

        ViewBag.State = state;
        ViewBag.Category = category;
        return View(await query.OrderBy(place => place.Name).ThenBy(place => place.Id).ToListAsync());
    }

    [HttpGet]
    public IActionResult Create() => View(new PlaceInput());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PlaceInput input)
    {
        ValidateInput(input);
        if (!ModelState.IsValid) return View(input);

        var now = DateTime.UtcNow;
        var place = new Place { IsActive = true, CreatedAt = now, UpdatedAt = now };
        input.ApplyTo(place);
        context.Places.Add(place);
        await context.SaveChangesAsync();
        TempData["PlaceSuccess"] = "Lokacija je dodana in vidna na zemljevidu.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var place = await context.Places.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();
        ViewBag.PlaceId = id;
        ViewBag.IsActive = place.IsActive;
        return View(PlaceInput.FromPlace(place));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PlaceInput input)
    {
        var place = await context.Places.SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();

        ValidateInput(input);
        if (!ModelState.IsValid)
        {
            ViewBag.PlaceId = id;
            ViewBag.IsActive = place.IsActive;
            return View(input);
        }

        input.ApplyTo(place);
        place.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        TempData["PlaceSuccess"] = "Spremembe lokacije so shranjene.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var place = await context.Places.SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();
        if (place.IsActive != isActive)
        {
            place.IsActive = isActive;
            place.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        TempData["PlaceSuccess"] = isActive ? "Lokacija je ponovno aktivna." : "Lokacija je deaktivirana.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateInput(PlaceInput input)
    {
        foreach (var (field, message) in input.Validate())
            ModelState.AddModelError(field, message);
    }
}
