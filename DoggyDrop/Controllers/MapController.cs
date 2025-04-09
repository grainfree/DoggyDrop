using Microsoft.AspNetCore.Mvc;
using DoggyDrop.Data;
using DoggyDrop.Models;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;


namespace DoggyDrop.Controllers
{
    public class MapController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<IdentityUser> _userManager;


        public MapController(ApplicationDbContext context, IWebHostEnvironment environment, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _environment = environment;
            _userManager = userManager;
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
                IsApproved = User.IsInRole("Admin"),
                UserId = User?.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null
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
        // =============================
        // Vrni vse odobrene koše v JSON formatu za iskanje najbližjega
        // =============================
        [HttpGet]
        public IActionResult FindNearest()
        {
            var bins = _context.TrashBins
                .Where(t => t.IsApproved)
                .Select(b => new
                {
                    b.Name,
                    b.Latitude,
                    b.Longitude
                })
                .ToList();

            return Json(bins);
        }
        [HttpGet]
        public IActionResult GetNearestBin(double latitude, double longitude)
        {
            var nearestBin = _context.TrashBins
                .Where(b => b.IsApproved)
                .OrderBy(b =>
                    Math.Pow(b.Latitude - latitude, 2) + Math.Pow(b.Longitude - longitude, 2)
                )
                .FirstOrDefault();

            if (nearestBin == null)
                return NotFound();

            return Json(new
            {
                nearestBin.Name,
                nearestBin.Latitude,
                nearestBin.Longitude,
                nearestBin.ImageUrl
            });
        }
        [HttpGet]
        public IActionResult MySubmissions()
        {
            if (!User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Login", "Account", new { area = "Identity" });
            }

            var userId = _userManager.GetUserId(User);

            var myBins = _context.TrashBins
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.DateAdded)
                .ToList();

            return View(myBins);
        }

        [HttpGet]
        public async Task<IActionResult> MyBins()
        {
            if (!User.Identity.IsAuthenticated)
                return RedirectToAction("Index");

            var userId = _userManager.GetUserId(User);

            var myBins = await _context.TrashBins
                .Where(b => b.UserId == userId)
                .ToListAsync();

            ViewBag.BinCount = myBins.Count;

            return View(myBins);
        }



    }
}


