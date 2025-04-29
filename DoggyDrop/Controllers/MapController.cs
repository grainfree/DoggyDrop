using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace DoggyDrop.Controllers
{
    public class MapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICloudinaryService _cloudinaryService;

        public MapController(ApplicationDbContext context,
                             IWebHostEnvironment environment,
                             UserManager<ApplicationUser> userManager,
                             ICloudinaryService cloudinaryService)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
            _cloudinaryService = cloudinaryService;
        }

        // 📍 Prikaz obrazca za dodajanje koša
        public IActionResult Add() => View();

        // 📍 Shrani novi koš
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(TrashBinViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            string? imageUrl = null;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                imageUrl = await _cloudinaryService.UploadTrashBinImageAsync(model.ImageFile);
            }

            var newBin = new TrashBin
            {
                Name = model.Name,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
                ImageUrl = imageUrl,
                DateAdded = DateTime.UtcNow,
                IsApproved = User.IsInRole("Admin"),
                UserId = _userManager.GetUserId(User)
            };

            _context.TrashBins.Add(newBin);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Koš je bil uspešno dodan!";
            return RedirectToAction("Index");
        }

        // 🗺️ Glavna stran z zemljevidom
        public IActionResult Index()
        {
            var bins = _context.TrashBins
                .Where(b => b.IsApproved)
                .ToList();

            return View(bins);
        }

        // ✅ Upravljanje - prikaz neodobrenih predlogov
        [Authorize(Roles = "Admin")]
        public IActionResult Manage()
        {
            var pendingBins = _context.TrashBins
                .Where(b => !b.IsApproved)
                .OrderByDescending(b => b.DateAdded)
                .ToList();

            return View(pendingBins);
        }

        // ✅ Potrdi predlog
        [Authorize(Roles = "Admin")]
        public IActionResult Approve(int id)
        {
            var bin = _context.TrashBins.Find(id);
            if (bin != null)
            {
                bin.IsApproved = true;
                _context.SaveChanges();
            }

            return RedirectToAction("Manage");
        }

        // ❌ Zavrni ali izbriši predlog z dinamičnim redirectom
        [Authorize]
        public IActionResult Reject(int id, string? returnTo)
        {
            var bin = _context.TrashBins.Find(id);
            if (bin != null)
            {
                _context.TrashBins.Remove(bin);
                _context.SaveChanges();
            }

            if (!string.IsNullOrEmpty(returnTo) && returnTo.ToLower() == "mybins")
                return RedirectToAction("MyBins");

            return RedirectToAction("Manage");
        }

        // 🔥 Admin ročno brisanje
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null)
                return NotFound();

            _context.TrashBins.Remove(bin);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Koš je bil uspešno izbrisan!";
            return RedirectToAction("Manage");
        }

        // 👤 Moji predlogi
        [Authorize]
        public async Task<IActionResult> MyBins()
        {
            var userId = _userManager.GetUserId(User);
            var myBins = await _context.TrashBins
                .Where(b => b.UserId == userId)
                .ToListAsync();

            ViewBag.BinCount = myBins.Count;
            return View(myBins);
        }

        // 📍 API: Najdi najbližji koš
        [HttpGet]
        public IActionResult GetNearestBin(double latitude, double longitude)
        {
            var nearest = _context.TrashBins
                .Where(b => b.IsApproved)
                .OrderBy(b => Math.Pow(b.Latitude - latitude, 2) + Math.Pow(b.Longitude - longitude, 2))
                .FirstOrDefault();

            if (nearest == null) return NotFound();

            return Json(new
            {
                nearest.Name,
                nearest.Latitude,
                nearest.Longitude,
                nearest.ImageUrl
            });
        }

        // 📍 API: Vsi odobreni koši
        [HttpGet]
        public IActionResult FindNearest()
        {
            var bins = _context.TrashBins
                .Where(b => b.IsApproved)
                .Select(b => new
                {
                    b.Name,
                    b.Latitude,
                    b.Longitude,
                    b.ImageUrl
                })
                .ToList();

            return Json(bins);
        }

        // ✏️ Prikaz obrazca za urejanje koša
        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var bin = await _context.TrashBins.FindAsync(id);
            if (bin == null)
                return NotFound();

            var model = new TrashBinViewModel
            {
                Id = bin.Id,
                Name = bin.Name,
                Latitude = bin.Latitude,
                Longitude = bin.Longitude
            };

            return View(model);
        }

        // ✏️ Shrani spremembe koša
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(TrashBinViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var bin = await _context.TrashBins.FindAsync(model.Id);
            if (bin == null)
                return NotFound();

            bin.Name = model.Name;
            bin.Latitude = model.Latitude;
            bin.Longitude = model.Longitude;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var imageUrl = await _cloudinaryService.UploadTrashBinImageAsync(model.ImageFile);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    bin.ImageUrl = imageUrl;
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Koš uspešno posodobljen!";
            return RedirectToAction("Manage");
        }
    }
}
