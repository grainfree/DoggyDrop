using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

// All application walk/photo deletion paths collect media before database cascades.
public sealed class WalkDataDeletion(ApplicationDbContext db, IUserMediaCleanup media)
{
    public async Task<bool> DeletePhotoAsync(string userId, int walkId, int photoId)
    {
        string url;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var photo = await db.WalkPhotos.SingleOrDefaultAsync(x => x.Id == photoId && x.WalkId == walkId && x.UserId == userId && x.Walk!.OwnerId == userId);
            if (photo == null) return false;
            url = photo.ImageUrl;
            db.WalkPhotos.Remove(photo);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await media.CleanupAsync([url]);
        return true;
    }

    // No new delete endpoint: this is the safe lifecycle entry point for whole-walk deletion.
    public async Task<bool> DeleteWalkAsync(string userId, int walkId)
    {
        List<string> urls;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            if (db.Database.IsNpgsql())
                await db.Walks.FromSqlInterpolated($"SELECT * FROM \"Walks\" WHERE \"Id\" = {walkId} AND \"OwnerId\" = {userId} FOR UPDATE")
                    .AsNoTracking().ToListAsync();
            if (!await db.Walks.AnyAsync(x => x.Id == walkId && x.OwnerId == userId)) return false;
            urls = await db.WalkPhotos.Where(x => x.WalkId == walkId).Select(x => x.ImageUrl).ToListAsync();
            await db.Walks.Where(x => x.Id == walkId && x.OwnerId == userId).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        await media.CleanupAsync(urls);
        return true;
    }
}
