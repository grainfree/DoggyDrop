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
    private const string EditConflictMessage = "Lokacija je bila med urejanjem spremenjena. Osveži podatke in poskusi znova.";

    [HttpGet]
    public async Task<IActionResult> Index(string? state, PlaceCategory? category, int? sourceId = null, bool noSource = false, int page = 1)
    {
        if (page is < 1 or > 100000 || sourceId is <= 0) return BadRequest();
        var query = context.Places.AsNoTracking();
        if (state == "active") query = query.Where(place => place.IsActive);
        if (state == "inactive") query = query.Where(place => !place.IsActive);
        if (category is { } selected && Enum.IsDefined(selected))
            query = query.Where(place => place.Category == selected);

        if (noSource) query = query.Where(place => place.DataSourceId == null);
        else if (sourceId.HasValue) query = query.Where(place => place.DataSourceId == sourceId);

        ViewBag.State = state;
        ViewBag.Category = category;
        ViewBag.SourceId = sourceId; ViewBag.NoSource = noSource; ViewBag.Page = page;
        await LoadSourcesAsync();
        var rows = await query.Include(p => p.DataSource).OrderBy(place => place.Name).ThenBy(place => place.Id)
            .Skip((page - 1) * 100).Take(101).ToListAsync();
        ViewBag.HasNext = rows.Count > 100;
        return View(rows.Take(100).ToList());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadSourcesAsync();
        return View(new PlaceInput());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(PlaceInput input)
    {
        await ValidateInputAsync(input);
        if (!ModelState.IsValid) return View(input);

        var newLogo = await UploadLogoAsync(input);
        if (!ModelState.IsValid) return View(input);
        var now = PlaceUpdates.NextUpdatedAt(DateTime.MinValue);
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
        var place = await context.Places.AsNoTracking().Include(item => item.Amenities).SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();
        ViewBag.PlaceId = id;
        ViewBag.IsActive = place.IsActive;
        await LoadSourcesAsync();
        return View(PlaceInput.FromPlace(place, logoCloud.Value));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, PlaceInput input)
    {
        var place = await context.Places.Include(item => item.Amenities).SingleOrDefaultAsync(item => item.Id == id);
        if (place == null) return NotFound();

        // Reject stale/missing versions before validation or external logo work. The
        // SQL predicate below also protects against a writer racing this check.
        if (!input.TryOriginalUpdatedAt(out var originalUpdatedAt) || originalUpdatedAt.Ticks != place.UpdatedAt.Ticks)
            return EditConflict(id, input);
        context.Entry(place).Property(item => item.UpdatedAt).OriginalValue = originalUpdatedAt;

        input.LogoUrl = PlaceLogoDelivery.ForMarker(place.LogoUrl, logoCloud.Value);
        input.AmenitiesVerifiedAt = place.AmenitiesVerifiedAt;
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
        // Always update the parent, including amenity-only edits. EF checks the form's
        // original UpdatedAt and rolls back the entire save on conflict.
        place.UpdatedAt = PlaceUpdates.NextUpdatedAt(place.UpdatedAt);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException)
        {
            await CleanupAfterFailedSaveAsync(newLogo);
            return EditConflict(id, input);
        }
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

    private ViewResult EditConflict(int id, PlaceInput input)
    {
        ModelState.AddModelError(string.Empty, EditConflictMessage);
        ViewBag.PlaceId = id;
        ViewBag.EditConflict = true;
        return View("Edit", input);
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
            place.UpdatedAt = PlaceUpdates.NextUpdatedAt(place.UpdatedAt);
            try { await context.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException)
            {
                TempData["PlaceSuccess"] = EditConflictMessage;
                return RedirectToAction(nameof(Index));
            }
        }

        TempData["PlaceSuccess"] = isActive ? "Lokacija je ponovno aktivna." : "Lokacija je deaktivirana.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateInputAsync(PlaceInput input)
    {
        await LoadSourcesAsync();
        if (input.DataSourceId.HasValue && !await context.DataSources.AnyAsync(s => s.Id == input.DataSourceId))
            ModelState.AddModelError(nameof(input.DataSourceId), "Izberi veljaven vir podatkov.");
        foreach (var (field, message) in input.Validate())
            ModelState.AddModelError(field, message);
        if (input.LogoFile != null && !await PlaceLogoUploadPolicy.IsSupportedAsync(input.LogoFile))
            ModelState.AddModelError(nameof(input.LogoFile), "Izberi veljavno sliko PNG, JPG ali WebP do 5 MB.");
        if (input.LogoFile != null && input.RemoveLogo)
            ModelState.AddModelError(nameof(input.LogoFile), "Izberi nalaganje ali odstranitev logotipa, ne obojega.");
    }

    private async Task LoadSourcesAsync() => ViewBag.DataSources = await context.DataSources.AsNoTracking()
        .OrderBy(s => s.Name).Select(s => new SourceOption(s.Id, s.Name)).ToListAsync();

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
