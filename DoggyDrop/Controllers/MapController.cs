using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Data;
using DoggyDrop.Models;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace DoggyDrop.Controllers
{
    public class MapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;

        public MapController(ApplicationDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        // =============================
        // GET: Obrazec za dodajanje novega koša
        // =============================
        public IActionResult Add()
        {
            return View();
        }

        // =============================
        // POST: Obdelava oddanega obrazca
        // =============================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(TrashBinViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            string? imagePath = null;

            if (model.ImageFile != null && model.ImageFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(model.ImageFile.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ImageFile.CopyToAsync(fileStream);
                }

                imagePath = "/uploads/" + uniqueFileName;
            }

            var newBin = new TrashBin
            {
                Name = model.Name,
                Latitude = double.Parse(model.Latitude, CultureInfo.InvariantCulture),
                Longitude = double.Parse(model.Longitude, CultureInfo.InvariantCulture),
                ImageUrl = imagePath,
                DateAdded = DateTime.UtcNow,
                IsApproved = User.IsInRole("Admin")
            };

            _context.TrashBins.Add(newBin);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Koš je bil uspešno dodan!";

            return RedirectToAction("Index");
        }

        // =============================
        // Prikaz zemljevida z markerji
        // =============================
        public IActionResult Index()
        {
            var bins = _context.TrashBins
                .Where(b => b.IsApproved)
                .ToList();

            return View(bins);
        }

        // =============================
        // Pregled neodobrenih košev
        // =============================
        public IActionResult Manage()
        {
            var pendingBins = _context.TrashBins
                .Where(b => !b.IsApproved)
                .ToList();

            return View(pendingBins);
        }

        // =============================
        // Potrdi koš
        // =============================
        public IActionResult Approve(int id)
        {
            var bin = _context.TrashBins.FirstOrDefault(b => b.Id == id);
            if (bin != null)
            {
                bin.IsApproved = true;
                _context.SaveChanges();
            }

            return RedirectToAction("Manage");
        }

        // =============================
        // Zavrni ali izbriši koš
        // =============================
        public IActionResult Reject(int id)
        {
            var bin = _context.TrashBins.FirstOrDefault(b => b.Id == id);
            if (bin != null)
            {
                _context.TrashBins.Remove(bin);
                _context.SaveChanges();
            }

            return RedirectToAction("Manage");
        }
    }
}
