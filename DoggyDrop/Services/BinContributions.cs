using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class BinReviewException(string message) : Exception(message);

public sealed class BinContributions(ApplicationDbContext db, ICloudinaryService uploads,
    IBinPhotoStorage storage, IBinPhotoReferences references, ILogger<BinContributions> logger)
{
    public async Task<long> SubmitAsync(int binId, string userId, Guid requestId, BinContributionType type,
        BinIssueReason? reason, string? description, double? latitude, double? longitude, int? duplicateId, IFormFile? photo)
    {
        if (requestId == Guid.Empty || !Enum.IsDefined(type) || description?.Length > 1000)
            throw new BinReviewException("Predlog ni veljaven.");
        var replay = await db.BinContributions.AsNoTracking().SingleOrDefaultAsync(c => c.SubmittedByUserId == userId && c.RequestId == requestId);
        if (replay != null) return replay.Id;
        var bin = await db.TrashBins.AsNoTracking().PublicBins().SingleOrDefaultAsync(b => b.Id == binId)
            ?? throw new BinReviewException("Koš ni več javno na voljo.");
        if (type == BinContributionType.Issue)
        {
            if (!reason.HasValue || !Enum.IsDefined(reason.Value)) throw new BinReviewException("Izberi vrsto težave.");
            if (reason == BinIssueReason.OTHER && string.IsNullOrWhiteSpace(description)) throw new BinReviewException("Opiši težavo.");
            if (reason == BinIssueReason.WRONG_LOCATION && (!BinCommunityRules.Coordinates(latitude, longitude) ||
                DuplicateCandidates.Distance(bin.Latitude, bin.Longitude, latitude!.Value, longitude!.Value) < .1))
                throw new BinReviewException("Označi novo lokacijo v Sloveniji.");
            if (reason == BinIssueReason.DUPLICATE && (duplicateId == binId || !duplicateId.HasValue ||
                !await db.TrashBins.PublicBins().AnyAsync(b => b.Id == duplicateId)))
                throw new BinReviewException("Izberi drug obstoječ javni koš.");
            if (photo != null) throw new BinReviewException("Fotografijo oddaj kot ločen predlog.");
        }
        else if (photo == null || photo.Length is <= 0 or > BinPhotoUploadPolicy.MaxBytes)
            throw new BinReviewException(BinPhotoUploadPolicy.Error);

        string? uploaded = null;
        if (type == BinContributionType.Photo)
        {
            try { uploaded = await uploads.UploadTrashBinImageAsync(photo!); }
            catch (Exception) { logger.LogWarning("Community bin photo upload failed."); }
            if (string.IsNullOrWhiteSpace(uploaded)) throw new BinReviewException(BinPhotoUploadPolicy.Error);
        }
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await BinCommunityRules.LockAsync(db);
            replay = await db.BinContributions.SingleOrDefaultAsync(c => c.SubmittedByUserId == userId && c.RequestId == requestId);
            if (replay == null && type == BinContributionType.Issue)
                replay = await db.BinContributions.FirstOrDefaultAsync(c => c.SubmittedByUserId == userId && c.BinId == binId &&
                    c.Type == type && c.Reason == reason && c.Status == BinContributionStatus.Pending);
            if (replay != null)
            {
                await transaction.RollbackAsync();
                if (uploaded != null) await CleanupAsync(uploaded);
                return replay.Id;
            }
            var current = await db.TrashBins.AsNoTracking().PublicBins().SingleOrDefaultAsync(b => b.Id == binId);
            if (current == null || BinCommunityRules.Snapshot(current) != BinCommunityRules.Snapshot(bin))
                throw new BinReviewException("Podatki koša so se spremenili. Ponovno odpri obrazec.");
            var contribution = new BinContribution
            {
                BinId = binId, SubmittedByUserId = userId, RequestId = requestId, Type = type,
                Reason = type == BinContributionType.Issue ? reason : null, Description = description?.Trim(),
                ProposedLatitude = reason == BinIssueReason.WRONG_LOCATION && type == BinContributionType.Issue ? latitude : null,
                ProposedLongitude = reason == BinIssueReason.WRONG_LOCATION && type == BinContributionType.Issue ? longitude : null,
                PossibleDuplicateBinId = reason == BinIssueReason.DUPLICATE && type == BinContributionType.Issue ? duplicateId : null,
                ProposedPhotoUrl = uploaded, BinSnapshot = BinCommunityRules.Snapshot(bin)
            };
            db.BinContributions.Add(contribution);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return contribution.Id;
        }
        catch (BinReviewException)
        {
            if (uploaded != null) await CleanupAsync(uploaded);
            throw;
        }
        catch (Exception)
        {
            // Unknown commit outcome: never delete an upload that could have just become referenced.
            logger.LogWarning("Community submission persistence failed; any uploaded asset retained for reconciliation.");
            throw new BinReviewException("Izida shranjevanja ni mogoče potrditi. Preveri Moje prispevke in ponovi z istim obrazcem.");
        }
    }

    public async Task ReviewAsync(long id, string reviewer, bool accept, bool retire, string? note)
    {
        if (note?.Length > 1000) throw new BinReviewException("Opomba je predolga.");
        string? cleanup = null;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await BinCommunityRules.LockAsync(db);
            var item = await db.BinContributions.Include(c => c.Bin).SingleOrDefaultAsync(c => c.Id == id)
                ?? throw new BinReviewException("Prispevek ne obstaja.");
            if (item.Status != BinContributionStatus.Pending) throw new BinReviewException("Prispevek je že obravnavan.");
            var bin = item.Bin;
            if (accept)
            {
                if (!bin.IsApproved || bin.IsRetired || BinCommunityRules.Snapshot(bin) != item.BinSnapshot)
                    throw new BinReviewException("Koš je bil medtem spremenjen. Predloga ni mogoče samodejno potrditi; zavrni ga in zahtevaj nov predlog.");
                // Validate infrastructure concurrency even when resolving an issue needs no public mutation.
                db.Entry(bin).Property(b => b.Name).IsModified = true;
                if (item.Type == BinContributionType.Photo)
                {
                    if (string.IsNullOrEmpty(item.ProposedPhotoUrl)) throw new BinReviewException("Fotografija ni na voljo.");
                    cleanup = bin.ImageUrl;
                    bin.ImageUrl = item.ProposedPhotoUrl;
                }
                else if (item.Reason == BinIssueReason.WRONG_LOCATION)
                {
                    if (!BinCommunityRules.Coordinates(item.ProposedLatitude, item.ProposedLongitude) ||
                        await BinCommunityRules.DuplicateAsync(db, item.ProposedLatitude!.Value, item.ProposedLongitude!.Value, bin.Id))
                        throw new BinReviewException("Nova lokacija ni veljavna ali ima koš v razdalji do vključno 20 m. Potrebna je ročna razrešitev.");
                    bin.Latitude = item.ProposedLatitude.Value; bin.Longitude = item.ProposedLongitude.Value;
                }
                else if (item.Reason == BinIssueReason.BIN_MISSING) bin.IsRetired = true;
                else if (item.Reason is BinIssueReason.NOT_PUBLIC or BinIssueReason.DUPLICATE)
                {
                    if (item.Reason == BinIssueReason.DUPLICATE && !await db.TrashBins.PublicBins().AnyAsync(b => b.Id == item.PossibleDuplicateBinId && b.Id != bin.Id))
                        throw new BinReviewException("Drugi koš ni več javno na voljo.");
                    if (retire) bin.IsRetired = true;
                }
            }
            else if (item.Type == BinContributionType.Photo) cleanup = item.ProposedPhotoUrl;
            item.Status = accept ? BinContributionStatus.Approved : BinContributionStatus.Rejected;
            item.ReviewedAt = DateTime.UtcNow; item.ReviewedByUserId = reviewer; item.ReviewNote = note?.Trim();
            if (item.SubmittedByUserId != null)
                db.UserNotifications.Add(new UserNotification { UserId = item.SubmittedByUserId, Type = "BinContributionReviewed",
                    Title = "Prispevek je pregledan", Body = accept ? "Tvoj prispevek je odobren." : "Tvoj prispevek je zavrnjen.",
                    LinkUrl = "/BinContributions/Mine", SourceKey = $"BinContribution:{item.Id}", CreatedAt = DateTime.UtcNow });
            try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
            catch (DbUpdateConcurrencyException) { throw new BinReviewException("Podatki so bili medtem spremenjeni. Osveži pregled."); }
        }
        if (cleanup != null) await CleanupAsync(cleanup);
    }

    public async Task LifecycleAsync(int id, string snapshot, bool retired)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await BinCommunityRules.LockAsync(db);
        var bin = await db.TrashBins.SingleOrDefaultAsync(b => b.Id == id) ?? throw new BinReviewException("Koš ne obstaja.");
        if (!bin.IsApproved || BinCommunityRules.Snapshot(bin) != snapshot) throw new BinReviewException("Stanje je zastarelo ali koš ni odobren.");
        if (!retired && await BinCommunityRules.DuplicateAsync(db, bin.Latitude, bin.Longitude, bin.Id))
            throw new BinReviewException("Ponovna aktivacija ima možnega dvojnika do vključno 20 m.");
        bin.IsRetired = retired;
        try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
        catch (DbUpdateConcurrencyException) { throw new BinReviewException("Koš je bil medtem spremenjen."); }
    }

    private async Task CleanupAsync(string url)
    {
        try { if (!await references.IsReferencedAsync(url)) await storage.DeleteManagedAsync(url); }
        catch (Exception) { logger.LogWarning("Community photo cleanup failed; review remains saved and asset retained."); }
    }
}
