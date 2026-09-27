using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public interface IBinPhotoReferences
{
    Task<bool> IsReferencedAsync(string url);
}
public sealed class BinPhotoReferences(DbContextOptions<ApplicationDbContext> options) : IBinPhotoReferences
{
    public async Task<bool> IsReferencedAsync(string url)
    {
        await using var fresh = new ApplicationDbContext(options);
        // Also retain an asset referenced through a delivery transform/version alias.
        var stem = Path.GetFileNameWithoutExtension(url[(url.LastIndexOf('/') + 1)..]);
        return await fresh.TrashBins.AsNoTracking().AnyAsync(b => b.ImageUrl != null && b.ImageUrl.Contains("/" + stem));
    }
}

public sealed record BinPhotoRotationResult(bool Success, bool Missing, string Message);

public sealed class BinPhotoRotationService(ApplicationDbContext db, IBinPhotoStorage storage, IBinPhotoReferences references,
    ILogger<BinPhotoRotationService> logger)
{
    public async Task<BinPhotoRotationResult> RotateAsync(int id, string? operation)
    {
        var degrees = operation switch { "left" => 270, "right" => 90, "half" => 180, _ => 0 };
        if (degrees == 0) return new(false, false, "Izberi veljaven zasuk fotografije.");
        var bin = await db.TrashBins.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id);
        if (bin == null) return new(false, true, "Koš ne obstaja.");
        if (!storage.CanRotate(bin.ImageUrl)) return new(false, false, "Fotografija manjka ali ni podprta upravljana slika. Naloži nadomestno fotografijo.");
        string? replacement;
        try { replacement = await storage.RotateCopyAsync(bin.ImageUrl!, degrees); }
        catch (Exception) { logger.LogWarning("Bin photo processing/upload failed for bin {BinId}.", id); replacement = null; }
        if (replacement == null) return new(false, false, "Fotografije ni bilo mogoče prebrati ali naložiti. Obstoječa slika je ohranjena; poskusi znova ali jo zamenjaj.");
        try
        {
            // Compare-and-swap only the image: no coordinates, approval or reward fields change.
            var changed = await db.TrashBins.Where(b => b.Id == id && b.ImageUrl == bin.ImageUrl)
                .ExecuteUpdateAsync(update => update.SetProperty(b => b.ImageUrl, replacement));
            if (changed == 0)
            {
                await CleanupIfUnused(replacement);
                return new(false, false, "Fotografija je bila medtem spremenjena. Preveri trenutno sliko in ponovi zasuk.");
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Bin photo database update failed or had an unknown outcome for bin {BinId}.", id);
            // The server may still commit after a connection failure. Even an immediate
            // fresh read cannot disprove that outcome: retain both assets for reconciliation.
            return new(false, false, "Izida shranjevanja ni bilo mogoče potrditi. Preveri trenutno fotografijo pred ponovnim zasukom.");
        }
        await CleanupIfUnused(bin.ImageUrl!);
        return new(true, false, "Fotografija je obrnjena in shranjena.");
    }

    private async Task CleanupIfUnused(string url)
    {
        try { if (!await references.IsReferencedAsync(url)) await storage.DeleteManagedAsync(url); }
        catch (Exception) { logger.LogWarning("Bin photo cleanup/reference check failed; asset retained."); }
    }
}
