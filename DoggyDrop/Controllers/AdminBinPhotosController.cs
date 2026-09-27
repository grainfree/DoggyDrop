using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminBinPhotosController(BinPhotoRotationService photos) : Controller
{
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16384)]
    public async Task<IActionResult> Rotate(int id, string? operation)
    {
        if (!ModelState.IsValid || operation is not ("left" or "right" or "half")) return BadRequest("Izberi veljaven zasuk.");
        var result = await photos.RotateAsync(id, operation);
        if (result.Missing) return NotFound(result.Message);
        TempData["BinPhotoMessage"] = result.Message;
        return RedirectToAction("Edit", "Map", new { id });
    }
}
