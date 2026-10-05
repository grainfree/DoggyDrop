using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public static class ActivityEmails
{
    // Does not save or send: the caller owns the business transaction/SaveChanges.
    public static async Task QueueAsync(ApplicationDbContext db, ActivityEmailType type, string? userId,
        int binId, long? contributionId = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(userId) || !await db.Users.AnyAsync(u => u.Id == userId && u.EmailConfirmed, ct) ||
            await db.NotificationPreferences.AnyAsync(p => p.UserId == userId && !p.ContributionUpdates, ct)) return;
        var key = contributionId.HasValue ? $"Contribution:{contributionId}:{type}" : $"Bin:{binId}:{type}";
        if (db.NotificationOutbox.Local.Any(x => x.EventKey == key) || await db.NotificationOutbox.AnyAsync(x => x.EventKey == key, ct)) return;
        var now = DateTime.UtcNow;
        db.NotificationOutbox.Add(new NotificationOutbox { RecipientUserId = userId, Type = type,
            BinId = binId, ContributionId = contributionId, EventKey = key, CreatedAt = now, NextAttemptAt = now });
    }

    public static ActivityEmailType ReviewType(BinContribution contribution, bool accepted) => contribution.Type == BinContributionType.Photo
        ? accepted ? ActivityEmailType.PhotoApproved : ActivityEmailType.PhotoRejected
        : contribution.Reason == BinIssueReason.WRONG_LOCATION
            ? accepted ? ActivityEmailType.LocationApproved : ActivityEmailType.LocationRejected
            : accepted ? ActivityEmailType.IssueApproved : ActivityEmailType.IssueRejected;
}
