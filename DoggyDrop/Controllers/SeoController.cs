using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SeoController(ApplicationDbContext db, IConfiguration configuration, IHostEnvironment environment) : Controller
{
    [Route("/Home/Error")]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return View("~/Views/Shared/Error.cshtml");
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots()
    {
        var site = new SeoSite(configuration, environment);
        var rules = site.AllowsIndexing(HttpContext) ? "User-agent: *\nAllow: /\n" : "User-agent: *\nDisallow: /\n";
        return Content(rules + "Sitemap: " + site.Absolute("/sitemap.xml") + "\n", "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    public async Task<IActionResult> Sitemap(CancellationToken cancellationToken)
    {
        var site = new SeoSite(configuration, environment);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var root = new XElement(ns + "urlset");
        // Preview/staging hosts do not publish a competing catalogue.
        if (site.AllowsIndexing(HttpContext))
        {
            foreach (var path in new[] { SeoMetadata.Home.CanonicalPath, SeoMetadata.Discovery.CanonicalPath,
                SeoMetadata.ProjectPath, SeoMetadata.MunicipalitiesPath })
                root.Add(new XElement(ns + "url", new XElement(ns + "loc", site.Absolute(path))));
            var places = await db.Places.AsNoTracking().ForPublicDetails().OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.Name, p.UpdatedAt }).ToListAsync(cancellationToken);
            foreach (var place in places.Where(p => PublicPlaceEligibility.ValidName(p.Name)))
            {
                var url = new XElement(ns + "url", new XElement(ns + "loc", site.Absolute(SeoMetadata.PlacePath(place.Id, place.Name))));
                if (place.UpdatedAt != default)
                    url.Add(new XElement(ns + "lastmod", DateTime.SpecifyKind(place.UpdatedAt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)));
                root.Add(url);
            }
        }
        return Content(new XDocument(root).ToString(), "application/xml", Encoding.UTF8);
    }
}
