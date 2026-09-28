using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public interface IPlaceLogoReferenceReader
{
    Task<bool> IsReferencedAsync(string logoUrl);

    // Default keeps existing readers compatible; the database reader batches this lookup.
    async Task<IReadOnlySet<PlaceLogoAsset>> FindReferencedAsync(IReadOnlyCollection<string> logoUrls)
    {
        var referenced = new HashSet<PlaceLogoAsset>();
        foreach (var url in logoUrls.Distinct(StringComparer.Ordinal))
        {
            if (!PlaceLogoDelivery.TryManagedAsset(url, null, out var asset))
                throw new InvalidOperationException("Unresolved Place logo cleanup candidate.");
            if (await IsReferencedAsync(url)) referenced.Add(asset);
        }
        return referenced;
    }
}

public sealed class PlaceLogoReferenceReader(DbContextOptions<ApplicationDbContext> options) : IPlaceLogoReferenceReader
{
    public const int MaxReferenceUrls = 4096;

    public async Task<IReadOnlySet<PlaceLogoAsset>> FindReferencedAsync(IReadOnlyCollection<string> logoUrls)
    {
        if (logoUrls.Count > AdminBulkTools.MaxSelection) throw new InvalidOperationException("Too many cleanup candidates.");
        var candidates = new HashSet<PlaceLogoAsset>();
        foreach (var url in logoUrls)
        {
            if (!PlaceLogoDelivery.TryManagedAsset(url, null, out var asset))
                throw new InvalidOperationException("Unresolved Place logo cleanup candidate.");
            candidates.Add(asset);
        }
        if (candidates.Count == 0) return candidates;
        await using var freshContext = new ApplicationDbContext(options);
        // One bounded projection, not one query per Place or asset. At the cap, refuse
        // cleanup rather than treating an incomplete view of references as absence.
        var urls = await freshContext.Places.AsNoTracking().Where(p => p.LogoUrl != null && p.LogoUrl != "")
            .Select(p => p.LogoUrl!).Distinct().Take(MaxReferenceUrls + 1).ToListAsync();
        if (urls.Count > MaxReferenceUrls) throw new InvalidOperationException("Place logo reference limit exceeded; retain assets.");
        var live = new HashSet<PlaceLogoAsset>();
        foreach (var url in urls)
        {
            var asset = PlaceLogoDelivery.ResolveAsset(url, null, out var uncertain);
            if (uncertain) throw new InvalidOperationException("Unresolved Cloudinary reference; retain assets.");
            if (asset != null && candidates.Contains(asset)) live.Add(asset);
        }
        return live;
    }

    public async Task<bool> IsReferencedAsync(string logoUrl)
    {
        if (!PlaceLogoDelivery.TryManagedAsset(logoUrl, null, out var asset)) return true;
        return (await FindReferencedAsync([logoUrl])).Contains(asset);
    }
}
