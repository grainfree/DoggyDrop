using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq.Expressions;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public static class BinCommunityRules
{
    public static readonly Expression<Func<TrashBin, bool>> Public = b => b.IsApproved && !b.IsRetired;
    public static IQueryable<TrashBin> PublicBins(this IQueryable<TrashBin> bins) => bins.Where(Public);
    public static IQueryable<TrashBin> PendingBins(this IQueryable<TrashBin> bins) =>
        bins.Where(b => !b.IsApproved && !b.IsRetired && !b.IsRejected);
    public static bool Coordinates(double? lat, double? lon) => lat.HasValue && lon.HasValue &&
        double.IsFinite(lat.Value) && double.IsFinite(lon.Value) && lat is >= 45.4 and <= 46.9 && lon is >= 13.3 and <= 16.7;
    // No usage counters in the fingerprint: using a bin does not stale an infrastructure review.
    public static string Snapshot(TrashBin b) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { b.Id, b.Name, b.Latitude, b.Longitude, b.ImageUrl, b.IsApproved, b.IsRetired, b.IsRejected, b.DataSourceId, b.UserId }))));
    public static async Task LockAsync(ApplicationDbContext db)
    {
        // Shared with the existing municipal importer; caller owns the transaction.
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(194721, 19)");
    }
    public static async Task<bool> DuplicateAsync(ApplicationDbContext db, double lat, double lon, int except = 0)
    {
        var rows = await db.TrashBins.AsNoTracking().Where(b => b.Id != except && !b.IsRetired && !b.IsRejected &&
            b.Latitude >= lat - .001 && b.Latitude <= lat + .001 && b.Longitude >= lon - .001 && b.Longitude <= lon + .001)
            .Select(b => new { b.Latitude, b.Longitude }).ToListAsync();
        return rows.Any(b => DuplicateCandidates.Distance(lat, lon, b.Latitude, b.Longitude) <= DuplicateCandidates.BinMetres);
    }
    public static string Reason(BinIssueReason? reason) => reason switch
    {
        BinIssueReason.BIN_MISSING => "Koša ni več", BinIssueReason.WRONG_LOCATION => "Napačna lokacija",
        BinIssueReason.DAMAGED => "Koš je poškodovan", BinIssueReason.NOT_PUBLIC => "Koš ni javno dostopen",
        BinIssueReason.DUPLICATE => "Ta koš je dvojnik", _ => "Drugo"
    };
    public static string Status(BinContributionStatus status) => status switch
    { BinContributionStatus.Pending => "V pregledu", BinContributionStatus.Approved => "Odobreno", _ => "Zavrnjeno" };
    public static string? PublicSourceLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
}
