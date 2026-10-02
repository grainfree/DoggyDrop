using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace DoggyDrop.Controllers;

[Authorize(Roles="Admin")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class AdminWaterPointsController(ApplicationDbContext db, WaterImportService importer) : Controller
{
    private async Task Sources() => ViewBag.Sources = await db.DataSources.AsNoTracking().OrderBy(s=>s.Name)
        .Select(s=>new ImportSource(s.Id,s.Name,s.Type,s.DataDate)).ToListAsync();
    [HttpGet] public async Task<IActionResult> Index(string? q, string? status, int? sourceId, int page=1) {
        var query=db.WaterPoints.AsNoTracking();
        if(!string.IsNullOrWhiteSpace(q)) query=query.Where(p=>p.Name!=null && p.Name.Contains(q));
        query=status switch { "active"=>query.Where(p=>p.IsApproved&&!p.IsRetired),"pending"=>query.Where(p=>!p.IsApproved&&!p.IsRetired),"retired"=>query.Where(p=>p.IsRetired),_=>query };
        if(sourceId.HasValue) query=query.Where(p=>p.DataSourceId==sourceId);
        page=Math.Clamp(page,1,100000);ViewBag.Page=page;ViewBag.Total=await query.CountAsync();
        ViewBag.Query=q;ViewBag.Status=status;ViewBag.SourceId=sourceId;await Sources();
        return View(await query.OrderBy(p=>p.Id).Skip((page-1)*100).Take(100).ToListAsync());
    }
    [HttpGet] public async Task<IActionResult> Create(){await Sources();return View("Edit",new WaterPointInput());}
    [HttpGet] public async Task<IActionResult> Edit(int id){var point=await db.WaterPoints.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id);if(point==null)return NotFound();
        ViewBag.Point=point;await Sources();return View(WaterPointInput.From(point));}
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(16384)] public Task<IActionResult> Create(WaterPointInput input)=>Save(null,input);
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(16384)] public Task<IActionResult> Edit(int id,WaterPointInput input)=>Save(id,input);
    private async Task<IActionResult> Save(int? id,WaterPointInput input) {
        await using var tx=await db.Database.BeginTransactionAsync();await WaterPoints.LockAsync(db);
        var point=id.HasValue?await db.WaterPoints.SingleOrDefaultAsync(p=>p.Id==id):new WaterPoint();
        if(point==null)return NotFound();
        if(id.HasValue && (input.OriginalUpdatedAt==null || input.OriginalUpdatedAt.Value!=point.UpdatedAt))return Conflict("Zapis je bil spremenjen. Osveži obrazec.");
        if(input.DataSourceId.HasValue&&!await db.DataSources.AnyAsync(s=>s.Id==input.DataSourceId))ModelState.AddModelError(nameof(input.DataSourceId),"Vir ne obstaja.");
        if(ModelState.IsValid && (!id.HasValue || input.Latitude!=point.Latitude || input.Longitude!=point.Longitude)) {
            // Coordinate duplicate validation also applies to pending records without inventing potability.
            var row=new WaterImportRow(2,input.Name??"",input.Latitude,input.Longitude,WaterAccess.Unknown,0,0,WaterPotability.SourceReportedDrinking,null,ImportRowStatus.Ready,[]);
            try {
                var classified=await importer.ClassifyAsync([row],HttpContext.RequestAborted,id);
                if(classified[0].Status!=ImportRowStatus.Ready)ModelState.AddModelError("","Možni dvojnik v razdalji do 15 m. Preveri obstoječi zapis, tudi umaknjen.");
            } catch(BinImportException error) { ModelState.AddModelError("",error.Message); }
        }
        if(!ModelState.IsValid){ViewBag.Point=id.HasValue?point:null;await Sources();return View("Edit",input);}
        var now=PlaceUpdates.NextUpdatedAt(point.UpdatedAt);input.ApplyTo(point);point.UpdatedAt=now;
        if(point.IsApproved)point.ApprovedAt??=now;
        if(!id.HasValue){point.DateAdded=now;db.WaterPoints.Add(point);}
        try {await db.SaveChangesAsync();await tx.CommitAsync();}
        catch(DbUpdateConcurrencyException){return Conflict("Zapis je bil spremenjen. Osveži obrazec.");}
        catch(DbUpdateException){return Conflict("Podatkov ni bilo mogoče shraniti. Preveri vir in osveži obrazec.");}
        return RedirectToAction(nameof(Edit),new{id=point.Id});
    }
    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4096)]
    public async Task<IActionResult> Retire(int id,DateTime? originalUpdatedAt,bool retired) {
        if(!ModelState.IsValid || originalUpdatedAt==null)return BadRequest();
        await using var tx=await db.Database.BeginTransactionAsync();await WaterPoints.LockAsync(db);
        var point=await db.WaterPoints.SingleOrDefaultAsync(p=>p.Id==id);if(point==null)return NotFound();
        if(point.UpdatedAt!=originalUpdatedAt.Value)return Conflict("Zapis je bil spremenjen. Osveži obrazec.");
        point.IsRetired=retired;point.UpdatedAt=PlaceUpdates.NextUpdatedAt(point.UpdatedAt);
        try{await db.SaveChangesAsync();await tx.CommitAsync();}catch(DbUpdateConcurrencyException){return Conflict("Zapis je bil spremenjen.");}
        return RedirectToAction(nameof(Edit),new{id});
    }
}
