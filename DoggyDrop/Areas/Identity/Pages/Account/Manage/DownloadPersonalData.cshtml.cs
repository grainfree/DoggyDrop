using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DoggyDrop.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DownloadPersonalDataModel(UserManager<ApplicationUser> users, PersonalDataExport export) : PageModel
{
    public IActionResult OnGet() => NotFound();
    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        return new ExportResult(export, user.Id);
    }

    private sealed class ExportResult(PersonalDataExport exporter, string userId) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "application/json; charset=utf-8";
            response.Headers.ContentDisposition = "attachment; filename=DoggyDrop-osebni-podatki.json";
            response.Headers.CacheControl = "no-store, private, no-cache";
            response.Headers.Pragma = "no-cache";
            response.Headers.XContentTypeOptions = "nosniff";
            await exporter.WriteAsync(userId, response.Body, context.HttpContext.RequestAborted);
        }
    }
}
