using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class AdminBulkTools(ApplicationDbContext db)
{
    public const int MaxSelection = 100;

    public async Task<BulkPreview?> PreviewAsync(BulkInput input)
    {
        if (!Enum.IsDefined(input.Target) || !Enum.IsDefined(input.Action) ||
            input.Ids == null || input.Ids.Length is < 1 or > MaxSelection || input.Ids.Any(id => id <= 0) ||
            (input.Target == BulkTarget.Bins && input.Action is BulkAction.Activate or BulkAction.Deactivate)) return null;
        // Bins have approval/reward semantics, not an independent activation flag.
        if (input.Action != BulkAction.AssignSource && input.DataSourceId != null) return null;
        string? sourceName = null;
        if (input.Action == BulkAction.AssignSource)
        {
            sourceName = await db.DataSources.Where(s => s.Id == input.DataSourceId).Select(s => s.Name).SingleOrDefaultAsync();
            if (sourceName == null) return null;
        }
        var ids = input.Ids.Distinct().Order().ToArray();
        var rows = input.Target == BulkTarget.Places
            ? await db.Places.AsNoTracking().Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id).Select(p => new BulkRecord(p.Id, p.Name)).ToListAsync()
            : await db.TrashBins.AsNoTracking().Where(b => ids.Contains(b.Id)).OrderBy(b => b.Id).Select(b => new BulkRecord(b.Id, b.Name)).ToListAsync();
        if (rows.Count != ids.Length) return null;
        return new(new BulkInput { Ids = ids, Target = input.Target, Action = input.Action, DataSourceId = input.DataSourceId }, rows, sourceName);
    }

    public async Task<int> ApplyAsync(BulkInput input)
    {
        var preview = await PreviewAsync(input); // Revalidate current IDs/source, not just the confirmation UI.
        if (preview == null) throw new InvalidOperationException("Izbor ali vir ni več veljaven. Ponovi izbor.");
        var ids = preview.Input.Ids;
        if (input.Target == BulkTarget.Places)
        {
            var places = await db.Places.Where(p => ids.Contains(p.Id)).ToListAsync();
            if (places.Count != ids.Length) throw new InvalidOperationException("Izbor se je spremenil. Ponovi izbor.");
            foreach (var place in places)
            {
                if (input.Action is BulkAction.Activate or BulkAction.Deactivate) place.IsActive = input.Action == BulkAction.Activate;
                else place.DataSourceId = input.Action == BulkAction.ClearSource ? null : input.DataSourceId;
                place.UpdatedAt = PlaceUpdates.NextUpdatedAt(place.UpdatedAt);
                // No amenities are loaded or changed; their verification remains intact.
            }
        }
        else
        {
            var bins = await db.TrashBins.Where(b => ids.Contains(b.Id)).ToListAsync();
            if (bins.Count != ids.Length) throw new InvalidOperationException("Izbor se je spremenil. Ponovi izbor.");
            foreach (var bin in bins) bin.DataSourceId = input.Action == BulkAction.ClearSource ? null : input.DataSourceId;
        }
        // EF wraps all batches in one transaction. Place concurrency/FK failures roll everything back.
        await db.SaveChangesAsync();
        return ids.Length;
    }
}
