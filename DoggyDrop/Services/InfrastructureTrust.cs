using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed record InfrastructureTrustSummary(DateTime? LastConfirmedAt, int RecentUniqueConfirmers, string State, bool HasCurrentIssue);

public sealed class InfrastructureTrust(ApplicationDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan OlderWindow = TimeSpan.FromDays(180);
    public static InfrastructureTrustSummary Summarize(DateTime? last, int unique, bool issue, DateTime now) =>
        new(last, unique, issue ? "issue" : last == null ? "unconfirmed" : now - last <= RecentWindow ? "recent" :
            now - last <= OlderWindow ? "older" : "stale", issue);

    // SQL GROUP BY/MAX/COUNT DISTINCT; only one aggregate per infrastructure enters memory.
    // Deleted accounts retain anonymous historical observations, never independent-user counts.
    public async Task<Dictionary<int, InfrastructureTrustSummary>> BinsAsync(int[]? ids = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime; var since = now - RecentWindow;
        var publicBins = db.TrashBins.AsNoTracking().PublicBins();
        if (ids != null) publicBins = publicBins.Where(b => ids.Contains(b.Id));
        var evidence = await (from c in db.InfrastructureConfirmations.AsNoTracking()
                              join b in publicBins on c.TrashBinId equals b.Id
                              where c.EvidenceVersion == b.EvidenceVersion && c.CreatedAt <= now
                              group c by b.Id into g
                              select new { Id = g.Key, Last = g.Max(c => c.CreatedAt),
                                  Count = g.Where(c => c.CreatedAt >= since && c.UserId != null).Select(c => c.UserId).Distinct().Count() }).ToListAsync(ct);
        var issues = await publicBins.Where(b => b.FullReports >= 2 || b.MissingReports >= 2 ||
            db.BinContributions.Any(c => c.BinId == b.Id && c.Type == BinContributionType.Issue &&
                (c.Status == BinContributionStatus.Pending || (c.Status == BinContributionStatus.Approved &&
                    c.Reason != BinIssueReason.WRONG_LOCATION && c.Reason != BinIssueReason.BIN_MISSING))))
            .Select(b => b.Id).ToListAsync(ct);
        var result = evidence.ToDictionary(x => x.Id, x => Summarize(x.Last, x.Count, false, now));
        foreach (var id in issues) result[id] = result.TryGetValue(id, out var old)
            ? old with { State = "issue", HasCurrentIssue = true } : Summarize(null, 0, true, now);
        return result;
    }

    public async Task<Dictionary<int, InfrastructureTrustSummary>> WaterAsync(int[]? ids = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime; var since = now - RecentWindow;
        var points = db.WaterPoints.AsNoTracking().PublicWater();
        if (ids != null) points = points.Where(p => ids.Contains(p.Id));
        var rows = await (from c in db.InfrastructureConfirmations.AsNoTracking()
                          join p in points on c.WaterPointId equals p.Id
                          where c.EvidenceVersion == p.EvidenceVersion && c.CreatedAt <= now
                          group c by p.Id into g
                          select new { Id = g.Key, Last = g.Max(c => c.CreatedAt),
                              Count = g.Where(c => c.CreatedAt >= since && c.UserId != null).Select(c => c.UserId).Distinct().Count() }).ToListAsync(ct);
        return rows.ToDictionary(x => x.Id, x => Summarize(x.Last, x.Count, false, now));
    }
    public static InfrastructureTrustSummary Empty => new(null, 0, "unconfirmed", false);
}
