using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminBinImportController(ApplicationDbContext db, BinImportSessions sessions, BinImportService importer) : Controller
{
    private string? Owner => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private Task<List<ImportSource>> Sources() => db.DataSources.AsNoTracking().OrderBy(s => s.Name)
        .Select(s => new ImportSource(s.Id, s.Name, s.Type, s.DataDate)).ToListAsync();

    [HttpGet]
    public async Task<IActionResult> Index() => View(await Sources());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(BinImportCsv.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = BinImportCsv.MaxBytes, MemoryBufferThreshold = BinImportCsv.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(IFormFile? file, int sourceId, string? delimiter)
    {
        if (Owner == null) return Forbid();
        if (!await sessions.UploadGate.WaitAsync(0)) return BadRequest("Drug prenos je v teku. Poskusi znova.");
        try
        {
            if (!ModelState.IsValid || file == null || file.Length == 0 || file.Length > BinImportCsv.MaxBytes ||
                !file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                throw new BinImportException("Izberi neprazno datoteko CSV do 5 MiB in veljaven vir.");
            var source = (await Sources()).SingleOrDefault(s => s.Id == sourceId)
                ?? throw new BinImportException("Izberi obstoječ vir podatkov.");
            await using var stream = file.OpenReadStream();
            var csv = await BinImportCsv.ReadAsync(stream, delimiter, HttpContext.RequestAborted);
            var filename = file.FileName.Replace('\\', '/').Split('/').Last();
            if (filename.Length > 200) filename = filename[..200];
            var session = sessions.Add(Owner, filename, source, csv);
            return RedirectToAction(nameof(Map), new { id = session.Id });
        }
        catch (BinImportException error) { ModelState.AddModelError("", error.Message); return View("Index", await Sources()); }
        finally { sessions.UploadGate.Release(); }
    }

    [HttpGet]
    public Task<IActionResult> Map(string id) => With(id, s => Task.FromResult<IActionResult>(s.Csv == null ? BadRequest("Preslikava je že potrjena.") : View(s.Page())));

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public Task<IActionResult> Map(string id, int version, int? latitude, int? longitude, int? name, int? address) => With(id, async s =>
    {
        if (!ModelState.IsValid || s.Csv == null || version != s.Version || !latitude.HasValue || !longitude.HasValue) return BadRequest("Izberi stolpca za širino in dolžino.");
        try
        {
            var rows = BinImportMapping.Map(s.Csv, new(latitude.Value, longitude.Value, name, address));
            s.Rows = await importer.ClassifyAsync(rows, HttpContext.RequestAborted);
            s.Selected = s.Rows.Where(r => r.Status == ImportRowStatus.Ready).Select(r => r.Number).ToHashSet();
            s.Csv = null; // Drop every unused field immediately after mapping.
            s.Version++;
            return RedirectToAction(nameof(Preview), new { id });
        }
        catch (BinImportException error) { ModelState.AddModelError("", error.Message); return View(s.Page()); }
    });

    [HttpGet]
    public Task<IActionResult> Preview(string id, int page = 1) => With(id, s => Task.FromResult<IActionResult>(
        s.Csv != null || s.Result != null || page < 1 || page > Math.Max(1, (s.Rows.Count + 99) / 100)
            ? BadRequest("Predogled ni na voljo.") : View(s.Page(page))));

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384), RequestFormLimits(ValueCountLimit = 128)]
    public Task<IActionResult> Select(string id, int version, int page, int[]? rows, bool proceed = false) => With(id, s =>
    {
        rows ??= [];
        if (!ModelState.IsValid || s.Csv != null || s.Result != null || version != s.Version || page < 1 || page > (s.Rows.Count + 99) / 100 || rows.Length > 100)
            return Task.FromResult<IActionResult>(BadRequest("Izbor je zastarel ali neveljaven. Osveži predogled."));
        var allowed = s.Rows.Skip((page - 1) * 100).Take(100).Where(r => r.Status == ImportRowStatus.Ready).Select(r => r.Number).ToHashSet();
        if (rows.Any(r => !allowed.Contains(r))) return Task.FromResult<IActionResult>(BadRequest("Izbereš lahko le pripravljene vrstice prikazane strani."));
        s.Selected.ExceptWith(allowed); s.Selected.UnionWith(rows); s.Version++; s.ConfirmedVersion = null;
        if (proceed && s.Selected.Count > 0)
        {
            s.ConfirmedVersion = s.Version;
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Confirm), new { id }));
        }
        return Task.FromResult<IActionResult>(RedirectToAction(nameof(Preview), new { id, page }));
    });

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public Task<IActionResult> Prepare(string id, int version) => With(id, s =>
    {
        if (!ModelState.IsValid || s.Csv != null || s.Result != null || s.Version != version || s.Selected.Count == 0)
            return Task.FromResult<IActionResult>(BadRequest("Izberi vsaj eno pripravljeno vrstico v trenutnem predogledu."));
        s.ConfirmedVersion = s.Version;
        return Task.FromResult<IActionResult>(RedirectToAction(nameof(Confirm), new { id }));
    });

    [HttpGet]
    public Task<IActionResult> Confirm(string id) => With(id, s => Task.FromResult<IActionResult>(s.Result != null || s.ConfirmedVersion != s.Version
        ? BadRequest("Najprej preveri izbor.") : View(s.Page())));

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public Task<IActionResult> Apply(string id, int version) => With(id, async s =>
    {
        if (s.Result != null) return RedirectToAction(nameof(Result), new { id }); // Replay never writes again.
        if (!ModelState.IsValid || s.ConfirmedVersion != s.Version || version != s.Version) return BadRequest("Potrditev ni veljavna. Ponovno preveri izbor.");
        try
        {
            var selected = s.Rows.Where(r => s.Selected.Contains(r.Number)).ToArray();
            var count = await importer.ImportAsync(s.Source.Id, selected, HttpContext.RequestAborted);
            s.Result = new(count, s.Rows.Count(r => r.Status == ImportRowStatus.PossibleDuplicate),
                s.Rows.Count(r => r.Status == ImportRowStatus.Invalid), s.Rows.Count(r => r.Status == ImportRowStatus.Ready) - count);
            s.Rows = []; s.Selected.Clear(); s.ConfirmedVersion = null; s.Version++;
            return RedirectToAction(nameof(Result), new { id });
        }
        catch (Exception error) when (error is BinImportException or DbUpdateException)
        {
            s.ConfirmedVersion = null; s.Version++;
            TempData["ImportMessage"] = error is BinImportException ? error.Message : "Uvoza ni bilo mogoče shraniti. Nobena vrstica ni uvožena. Preveri vir in ponovi pregled.";
            return RedirectToAction(nameof(Preview), new { id });
        }
    });

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public Task<IActionResult> Refresh(string id, int version) => With(id, async s =>
    {
        if (!ModelState.IsValid || s.Csv != null || s.Result != null || version != s.Version) return BadRequest("Predogled je zastarel.");
        try
        {
            s.Rows = await importer.ClassifyAsync(s.Rows, HttpContext.RequestAborted);
            s.Selected.IntersectWith(s.Rows.Where(r => r.Status == ImportRowStatus.Ready).Select(r => r.Number));
            s.ConfirmedVersion = null; s.Version++;
        }
        catch (BinImportException error) { TempData["ImportMessage"] = error.Message; }
        return RedirectToAction(nameof(Preview), new { id });
    });

    [HttpGet]
    public Task<IActionResult> Result(string id) => With(id, s => Task.FromResult<IActionResult>(s.Result == null ? BadRequest() : View(s.Page())));

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public Task<IActionResult> Cancel(string id) => With(id, s =>
    {
        sessions.Remove(id, s.Owner); return Task.FromResult<IActionResult>(RedirectToAction(nameof(Index)));
    });

    private async Task<IActionResult> With(string id, Func<BinImportSession, Task<IActionResult>> action)
    {
        if (Owner == null) return Forbid();
        var session = sessions.Find(id, Owner);
        if (session == null) return NotFound("Uvoz je potekel ali ni na voljo. Ponovno naloži CSV.");
        await session.Gate.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (sessions.Find(id, Owner) != session) return NotFound("Uvoz je potekel. Ponovno naloži CSV.");
            return await action(session);
        }
        finally { session.Gate.Release(); }
    }
}
