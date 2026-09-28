using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed record PlaceLogoVersion(int Id, long UpdatedAtTicks);
public sealed record BulkLogoPlace(int Id, string Name, PlaceCategory Category, long UpdatedAtTicks);
public sealed record BulkLogoPage(IReadOnlyList<BulkLogoPlace> Places, string Token);
public sealed record BulkLogoTicket(PlaceLogoVersion[] Places, string Owner, DateTimeOffset ExpiresAt);
public sealed class BulkPlaceLogoException(string message) : Exception(message);

public sealed class BulkPlaceLogos(ApplicationDbContext db, IPlaceLogoStorage logos,
    IPlaceLogoReferenceReader references, PlaceLogoCloudName cloud, ILogger<BulkPlaceLogos> logger)
{
    public const string Changed = "Izbrane lokacije so se med urejanjem spremenile. Nič ni bilo prepisano. Osveži seznam in ponovi izbor.";

    public async Task<IReadOnlyList<BulkLogoPlace>> PreviewAsync(int[]? ids)
    {
        if (ids == null || ids.Length is < 1 or > AdminBulkTools.MaxSelection || ids.Any(id => id <= 0))
            throw new BulkPlaceLogoException("Izberi od 1 do 100 prikazanih lokacij.");
        var selected = ids.Distinct().Order().ToArray();
        var places = await db.Places.AsNoTracking().Where(p => selected.Contains(p.Id)).OrderBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.Category, p.UpdatedAt }).ToListAsync();
        if (places.Count != selected.Length) throw new BulkPlaceLogoException(Changed);
        if (places.Any(p => !PlaceCategories.Get(p.Category).IsCommercial)) throw new BulkPlaceLogoException(
            "Logotipi so na voljo samo za poslovne in storitvene lokacije. Odstrani parke, plaže in nepodprte kategorije iz izbora.");
        return places.Select(p => new BulkLogoPlace(p.Id, p.Name, p.Category, p.UpdatedAt.Ticks)).ToArray();
    }

    public async Task<int> ApplyAsync(IReadOnlyList<PlaceLogoVersion>? versions, IFormFile? file)
    {
        if (versions == null || versions.Count is < 1 or > AdminBulkTools.MaxSelection ||
            versions.Any(v => v.Id <= 0) || versions.Select(v => v.Id).Distinct().Count() != versions.Count)
            throw new BulkPlaceLogoException("Potrditev ni veljavna. Ponovi izbor.");
        var expected = versions.ToDictionary(v => v.Id, v => v.UpdatedAtTicks);
        var ids = expected.Keys.ToArray();
        var places = await db.Places.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).ToListAsync();
        if (places.Count != ids.Length || places.Any(p => p.UpdatedAt.Ticks != expected[p.Id] || !PlaceCategories.Get(p.Category).IsCommercial))
            throw new BulkPlaceLogoException(Changed);
        if (!await PlaceLogoUploadPolicy.IsSupportedAsync(file))
            throw new BulkPlaceLogoException("Izberi veljavno sliko PNG, JPG ali WebP do 5 MB.");

        string? uploaded;
        try { uploaded = await logos.UploadAsync(file!); } // ONE upload, before any database mutation.
        catch (Exception) { logger.LogWarning("Bulk Place logo upload failed for {Count} Places.", places.Count); throw new BulkPlaceLogoException("Logotipa ni bilo mogoče naložiti. Lokacije niso spremenjene. Ponovi izbor."); }
        if (!PlaceLogoDelivery.TryManagedId(uploaded, out _) || !Managed(uploaded)) throw new BulkPlaceLogoException("Logotipa ni bilo mogoče naložiti. Preveri veljavnost slike in nastavitev shrambe. Ponovi izbor.");

        PlaceLogoDelivery.TryManagedAsset(uploaded, cloud.Value, out var newAsset);
        var old = places.Select(p => p.LogoUrl).Where(url => Managed(url) &&
            PlaceLogoDelivery.TryManagedAsset(url, cloud.Value, out var asset) && asset != newAsset).Select(url => url!).ToArray();
        try
        {
            // Loading occurred before upload. EF retains original UpdatedAt for each row,
            // so a concurrent edit during upload/commit aborts the entire transaction.
            await using var transaction = await db.Database.BeginTransactionAsync();
            foreach (var place in places)
            {
                place.LogoUrl = uploaded;
                place.UpdatedAt = PlaceUpdates.NextUpdatedAt(place.UpdatedAt);
            }
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            // Transaction disposal precedes a NEW context's reference check. Commit may
            // have succeeded despite an exception; uncertainty must retain a live asset.
            db.ChangeTracker.Clear();
            await CleanupAsync([uploaded!]);
            logger.LogWarning("Bulk Place logo assignment failed or had an uncertain outcome for {Count} Places.", places.Count);
            throw new BulkPlaceLogoException("Shranjevanja ni bilo mogoče potrditi. Osveži seznam in preveri logotipe pred ponovnim izborom.");
        }
        await CleanupAsync(old); // Cleanup failure never rolls back a successful assignment.
        return places.Count;
    }

    private bool Managed(string? url) => !string.IsNullOrEmpty(cloud.Value) && PlaceLogoDelivery.TryManagedAsset(url, cloud.Value, out _);

    private async Task CleanupAsync(IReadOnlyCollection<string> urls)
    {
        var candidates = new Dictionary<PlaceLogoAsset, string>();
        foreach (var url in urls.Where(Managed))
            if (PlaceLogoDelivery.TryManagedAsset(url, cloud.Value, out var asset)) candidates.TryAdd(asset, url);
        if (candidates.Count == 0) return;
        IReadOnlySet<PlaceLogoAsset> live;
        try { live = await references.FindReferencedAsync(candidates.Values.ToArray()); }
        catch (Exception) { logger.LogWarning("Bulk Place logo reference check failed; {Count} assets retained.", candidates.Count); return; }
        foreach (var (asset, url) in candidates.Where(candidate => !live.Contains(candidate.Key)))
        {
            try { await logos.DeleteManagedAsync(url); }
            catch (Exception) { logger.LogWarning("Obsolete bulk Place logo cleanup failed; asset retained."); }
        }
    }
}
