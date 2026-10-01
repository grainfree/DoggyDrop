using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminBinsController(ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> ExportComparison(CancellationToken cancellationToken)
    {
        var snapshotAt = DateTime.UtcNow;
        // Explicit allowlist: no entities, contributor information or source navigation are loaded.
        var rows = await db.TrashBins.AsNoTracking().OrderBy(b => b.Id)
            .Select(b => new { b.Id, b.Latitude, b.Longitude, b.IsApproved, b.DateAdded, b.ApprovedAt, b.DataSourceId })
            .ToListAsync(cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine(CsvRow("BinId", "Latitude", "Longitude", "IsApproved", "DateAdded", "ApprovedAt", "DataSourceId"));
        foreach (var row in rows)
            csv.AppendLine(CsvRow(
                row.Id.ToString(CultureInfo.InvariantCulture),
                row.Latitude.ToString("R", CultureInfo.InvariantCulture),
                row.Longitude.ToString("R", CultureInfo.InvariantCulture),
                row.IsApproved ? "true" : "false",
                UtcDate(row.DateAdded),
                row.ApprovedAt.HasValue ? UtcDate(row.ApprovedAt.Value) : "",
                row.DataSourceId?.ToString(CultureInfo.InvariantCulture) ?? ""));

        Response.Headers.CacheControl = "private, no-store, no-cache";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8",
            $"doggydrop-trashbins-{snapshotAt.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.csv");
    }

    // Database timestamps are UTC; providers such as SQLite can return an unspecified Kind.
    private static string UtcDate(DateTime value) =>
        (value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime())
        .ToString("O", CultureInfo.InvariantCulture);

    private static string CsvRow(params string[] cells) => string.Join(",", cells.Select(value =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value));

    [HttpGet]
    public async Task<IActionResult> Index(string? state, int? sourceId, bool noSource = false, int page = 1)
    {
        if (page is < 1 or > 100000 || sourceId is <= 0) return BadRequest();
        var query = db.TrashBins.AsNoTracking();
        if (state == "approved") query = query.Where(b => b.IsApproved);
        if (state == "pending") query = query.Where(b => !b.IsApproved);
        if (noSource) query = query.Where(b => b.DataSourceId == null);
        else if (sourceId.HasValue) query = query.Where(b => b.DataSourceId == sourceId);
        var rows = await query.OrderBy(b => b.Id).Skip((page - 1) * 100).Take(101)
            .Select(b => new AdminBinRow(b.Id, b.Name, b.IsApproved, b.DataSource == null ? null : b.DataSource.Name)).ToListAsync();
        ViewBag.Page = page; ViewBag.HasNext = rows.Count > 100; ViewBag.State = state;
        ViewBag.SourceId = sourceId; ViewBag.NoSource = noSource;
        ViewBag.DataSources = await db.DataSources.AsNoTracking().OrderBy(s => s.Name).Select(s => new SourceOption(s.Id, s.Name)).ToListAsync();
        return View(rows.Take(100).ToList());
    }
}
