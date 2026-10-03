using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace DoggyDrop.Areas.Identity.Pages.Account.Manage;

[Authorize]
[ValidateAntiForgeryToken]
[EnableRateLimiting(PasswordSettings.Policy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SetPasswordModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Vnesi novo geslo.")]
        [DataType(DataType.Password)]
        [Display(Name = "Novo geslo")]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Ponovi novo geslo.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Novi gesli se ne ujemata.")]
        [Display(Name = "Ponovi novo geslo")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        return await users.HasPasswordAsync(user) ? RedirectToPage("./ChangePassword") : Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        // This existing Identity route only adds a FIRST password, never replaces one.
        if (await users.HasPasswordAsync(user)) return RedirectToPage("./ChangePassword");
        if (!ModelState.IsValid) return InvalidForm();
        var result = await users.AddPasswordAsync(user, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("Input.NewPassword", PasswordSettings.Error(error));
            return InvalidForm();
        }
        await signIn.RefreshSignInAsync(user);
        TempData["StatusMessage"] = "Geslo je bilo uspešno nastavljeno.";
        return RedirectToPage("./ChangePassword");
    }

    private PageResult InvalidForm()
    {
        Input = new();
        foreach (var key in ModelState.Keys.ToArray()) ModelState.SetModelValue(key, null, null);
        return Page();
    }
}
