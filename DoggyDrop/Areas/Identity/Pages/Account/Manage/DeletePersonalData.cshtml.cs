using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DoggyDrop.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class DeletePersonalDataModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    AccountDataDeletion deletion, ILogger<DeletePersonalDataModel> logger) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool RequirePassword { get; private set; }
    public sealed class InputModel
    {
        [DataType(DataType.Password)] public string? Password { get; set; }
        [Range(typeof(bool), "true", "true", ErrorMessage = "Potrdi, da želiš trajno izbrisati račun.")]
        public bool ConfirmDeletion { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        RequirePassword = await users.HasPasswordAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        RequirePassword = await users.HasPasswordAsync(user);
        if (RequirePassword && (string.IsNullOrEmpty(Input.Password) || !await users.CheckPasswordAsync(user, Input.Password)))
            ModelState.AddModelError("Input.Password", "Geslo ni pravilno.");
        if (!Input.ConfirmDeletion) ModelState.AddModelError("Input.ConfirmDeletion", "Potrdi, da želiš trajno izbrisati račun.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var result = await deletion.DeleteAsync(user);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Računa ni bilo mogoče izbrisati. Osveži stran in poskusi znova.");
                return Page();
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Account deletion database operation failed.");
            ModelState.AddModelError(string.Empty, "Računa ni bilo mogoče izbrisati. Poskusi znova pozneje.");
            return Page();
        }
        await signIn.SignOutAsync();
        return LocalRedirect("~/");
    }
}
