using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminPlacesController(
    ApplicationDbContext context, IPlaceLogoStorage logos,
    IPlaceLogoReferenceReader logoReferences, PlaceLogoCloudName logoCloud,
    ILogger<AdminPlacesController> logger) : Controller
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
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(PlaceInput input)
    {
        await ValidateInputAsync(input);
        if (!ModelState.IsValid) return View(input);

        var newLogo = await UploadLogoAsync(input);
        if (!ModelState.IsValid) return View(input);
        var now = DateTime.UtcNow;
        var place = new Place { IsActive = true, CreatedAt = now, UpdatedAt = now };
        input.ApplyTo(place);
        place.LogoUrl = newLogo;
        context.Places.Add(place);
        try { await context.SaveChangesAsync(); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not save place after logo upload.");
            await CleanupAfterFailedSaveAsync(newLogo);
            ModelState.AddModelError(string.Empty, "Lokacije ni bilo mogoče shraniti. Poskusi znova.");
            return View(input);
        }
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
        return View(PlaceInput.FromPlace(place, logoCloud.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, PlaceInput input)
    {
        var place = await context.Places.SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();

        input.LogoUrl = PlaceLogoDelivery.ForMarker(place.LogoUrl, logoCloud.Value);
        await ValidateInputAsync(input);
        if (!ModelState.IsValid)
        {
            ViewBag.PlaceId = id;
            ViewBag.IsActive = place.IsActive;
            return View(input);
        }

        var oldLogo = place.LogoUrl;
        var newLogo = await UploadLogoAsync(input);
        if (!ModelState.IsValid)
        {
            ViewBag.PlaceId = id;
            ViewBag.IsActive = place.IsActive;
            return View(input);
        }
        input.ApplyTo(place);
        if (newLogo != null || input.RemoveLogo) place.LogoUrl = newLogo;
        place.UpdatedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(); }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not update place after logo upload.");
            await CleanupAfterFailedSaveAsync(newLogo);
            ModelState.AddModelError(string.Empty, "Sprememb ni bilo mogoče shraniti. Poskusi znova.");
            ViewBag.PlaceId = id;
            ViewBag.IsActive = place.IsActive;
            return View(input);
        }
        if (oldLogo != place.LogoUrl) await DeleteLogoBestEffortAsync(oldLogo, checkReferences: true);
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

    private async Task ValidateInputAsync(PlaceInput input)
    {
        foreach (var (field, message) in input.Validate())
            ModelState.AddModelError(field, message);
        if (input.LogoFile != null && !await PlaceLogoUploadPolicy.IsSupportedAsync(input.LogoFile))
            ModelState.AddModelError(nameof(input.LogoFile), "Izberi veljavno sliko PNG, JPG ali WebP do 5 MB.");
        if (input.LogoFile != null && input.RemoveLogo)
            ModelState.AddModelError(nameof(input.LogoFile), "Izberi nalaganje ali odstranitev logotipa, ne obojega.");
    }

    private async Task<string?> UploadLogoAsync(PlaceInput input)
    {
        if (input.LogoFile == null) return null;
        try
        {
            var url = await logos.UploadAsync(input.LogoFile);
            if (PlaceLogoDelivery.TryManagedId(url, out _)) return url;
            ModelState.AddModelError(nameof(input.LogoFile), "Logotipa ni bilo mogoče naložiti. Preveri Cloudinary in poskusi znova.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Place logo upload failed.");
            ModelState.AddModelError(nameof(input.LogoFile), "Logotipa ni bilo mogoče naložiti. Poskusi znova.");
        }
        return null;
    }

    private async Task DeleteLogoBestEffortAsync(string? url, bool checkReferences = false)
    {
        if (!PlaceLogoDelivery.TryManagedId(url, out _)) return;
        try
        {
            if (checkReferences && await context.Places.AsNoTracking().AnyAsync(place => place.LogoUrl == url)) return;
            await logos.DeleteManagedAsync(url);
        }
        catch (Exception exception) { logger.LogWarning(exception, "Place logo cleanup failed."); }
    }

    private async Task CleanupAfterFailedSaveAsync(string? newLogo)
    {
        if (!PlaceLogoDelivery.TryManagedId(newLogo, out _)) return;
        try
        {
            if (!await logoReferences.IsReferencedAsync(newLogo!))
                await DeleteLogoBestEffortAsync(newLogo);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Place logo reference verification failed; uploaded asset was retained.");
        }
    }
}
