using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
namespace DoggyDrop.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class WaterPointsController(ApplicationDbContext db, IWalkingRoutes routes) : Controller
{
    [HttpGet("/api/waterpoints")]
    public async Task<IActionResult> Index(CancellationToken ct)=>Json(await WaterPoints.LoadAsync(db.WaterPoints,ct));
    [HttpGet("/api/waterpoints/{id:int}")]
    public async Task<IActionResult> Details(int id,CancellationToken ct){var p=(await WaterPoints.LoadAsync(db.WaterPoints.Where(p=>p.Id==id),ct)).SingleOrDefault();return p==null?NotFound():Json(p);}
    public sealed record RouteInput(WalkingRoutesController.CoordinateInput? Origin);
    [HttpPost("/api/waterpoints/{id:int}/route"),ValidateAntiForgeryToken,EnableRateLimiting("walking-route"),RequestSizeLimit(4096)]
    public async Task<IActionResult> Route(int id,[FromBody] RouteInput? input,CancellationToken ct) {
        var origin=input?.Origin?.ToCoordinate();
        if(!ModelState.IsValid || origin is not {IsValid:true})return BadRequest(new{error="invalid"});
        var target=await db.WaterPoints.AsNoTracking().PublicWater().Where(p=>p.Id==id).Select(p=>new{p.Latitude,p.Longitude}).SingleOrDefaultAsync(ct);
        if(target==null)return StatusCode(410,new{error="unavailable"});
        var result=await routes.RouteAsync([origin,new(target.Latitude,target.Longitude)],ct);
        if(!result.IsRouted){if(result.RetryAfterSeconds>0)Response.Headers.RetryAfter=result.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(result.Failure=="busy"?429:503,new{error=result.Failure});}
        return Json(new{points=result.Points.Select(p=>new[]{p.Latitude,p.Longitude}),distanceMeters=result.DistanceMeters,durationSeconds=result.DurationSeconds});
    }
}
