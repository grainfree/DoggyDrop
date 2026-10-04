using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class AccountDataDeletion(ApplicationDbContext db, UserManager<ApplicationUser> users, IUserMediaCleanup media)
{
    public async Task<IdentityResult> DeleteAsync(ApplicationUser user)
    {
        List<string?> mediaUrls;
        try
        {
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                // Existing dependent FK inserts must finish before the graph is collected. Later
                // inserts wait, then fail when the account is gone. No schema change is needed.
                if (db.Database.IsNpgsql())
                {
                    await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {user.Id} FOR UPDATE")
                        .AsNoTracking().ToListAsync();
                    await db.Dogs.FromSqlInterpolated($"SELECT * FROM \"Dogs\" WHERE \"OwnerId\" = {user.Id} FOR UPDATE")
                        .AsNoTracking().ToListAsync();
                    await db.Walks.FromSqlInterpolated($"SELECT * FROM \"Walks\" WHERE \"OwnerId\" = {user.Id} OR \"DogId\" IN (SELECT \"Id\" FROM \"Dogs\" WHERE \"OwnerId\" = {user.Id}) FOR UPDATE")
                        .AsNoTracking().ToListAsync();
                }

                mediaUrls = await db.Users.Where(x => x.Id == user.Id).Select(x => x.ProfileImageUrl).ToListAsync();
                mediaUrls.AddRange(await db.Dogs.Where(x => x.OwnerId == user.Id).Select(x => x.PhotoUrl).ToListAsync());
                mediaUrls.AddRange(await db.WalkPhotos.Where(x => x.UserId == user.Id || x.Walk!.OwnerId == user.Id || x.Walk.Dog!.OwnerId == user.Id)
                    .Select(x => (string?)x.ImageUrl).ToListAsync());
                mediaUrls.AddRange(await db.TrashBins.Where(x => x.UserId == user.Id && !x.IsApproved).Select(x => x.ImageUrl).ToListAsync());
                mediaUrls.AddRange(await db.BinContributions.Where(c => c.SubmittedByUserId == user.Id && c.Status == BinContributionStatus.Pending).Select(c => c.ProposedPhotoUrl).ToListAsync());
                // Remove private submitted text/evidence on account deletion; keep only accepted infrastructure history.
                await db.BinContributions.Where(c => c.SubmittedByUserId == user.Id && c.Status == BinContributionStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, BinContributionStatus.Rejected)
                        .SetProperty(c => c.ReviewedAt, DateTime.UtcNow).SetProperty(c => c.ProposedPhotoUrl, (string?)null));
                await db.BinContributions.Where(c => c.SubmittedByUserId == user.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Description, (string?)null).SetProperty(c => c.SubmittedByUserId, (string?)null));

                await db.Friendships.Where(x => x.RequesterId == user.Id || x.AddresseeId == user.Id).ExecuteDeleteAsync();
                // Formerly public bins can be unapproved again and still have protected
                // infrastructure history. Retain those hidden records, without ownership,
                // rather than failing the entire account deletion on a restricted FK.
                await db.TrashBins.Where(x => x.UserId == user.Id && !x.IsApproved
                    && !db.BinContributions.Any(c => c.BinId == x.Id)
                    && !db.InfrastructureConfirmations.Any(c => c.TrashBinId == x.Id)).ExecuteDeleteAsync();
                await db.TrashBins.Where(x => x.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserId, (string?)null));

                // No actor FK exists on legacy notifications. Obsolete walk broadcasts are
                // globally removed; the other actor-bearing types retain only safe metadata/text.
                // Never infer an actor by matching free text. This cleanup shares the transaction.
                await db.UserNotifications.Where(x => x.Type == NotificationPrivacy.ObsoleteWalkStart).ExecuteDeleteAsync();
                foreach (var type in NotificationPrivacy.ActorMessageTypes.Where(t => t != NotificationPrivacy.ObsoleteWalkStart))
                {
                    var title = NotificationPrivacy.NeutralTitle(type)!;
                    var body = NotificationPrivacy.NeutralBody(type)!;
                    var link = NotificationPrivacy.Link(type, null);
                    await db.UserNotifications.Where(x => x.Type == type)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, title)
                            .SetProperty(x => x.Body, body).SetProperty(x => x.LinkUrl, link)
                            .SetProperty(x => x.SourceKey, (string?)null));
                }

                var result = await users.DeleteAsync(user);
                if (!result.Succeeded)
                {
                    await transaction.RollbackAsync();
                    db.ChangeTracker.Clear();
                    return result;
                }
                await transaction.CommitAsync();
            }
        }
        catch
        {
            // Do not leave failed Deleted entries available to later code in this request.
            db.ChangeTracker.Clear();
            throw;
        }
        await media.CleanupAsync(mediaUrls);
        return IdentityResult.Success;
    }
}
