using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DoggyDrop.Controllers;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProjectController(IConfiguration configuration) : Controller
{
    [HttpGet(SeoMetadata.ProjectPath)]
    public IActionResult Index() => Page("Index", SeoMetadata.Project);

    [HttpGet(SeoMetadata.MunicipalitiesPath)]
    public IActionResult Municipalities() => Page("Municipalities", SeoMetadata.Municipalities);

    private ViewResult Page(string view, SeoMetadata metadata)
    {
        ViewData[SeoMetadata.ViewDataKey] = metadata;
        return View(view, PublicContact.Parse(configuration["DoggyDrop:ContactEmail"]));
    }
}
