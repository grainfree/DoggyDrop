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
public sealed class ChangePasswordModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [TempData] public string? StatusMessage { get; set; }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Vnesi trenutno geslo.")]
        [DataType(DataType.Password)]
        [Display(Name = "Trenutno geslo")]
        public string CurrentPassword { get; set; } = "";

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
        return await users.HasPasswordAsync(user) ? Page() : RedirectToPage("./SetPassword");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!await users.HasPasswordAsync(user)) return RedirectToPage("./SetPassword");
        if (!ModelState.IsValid) return InvalidForm();

        var result = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? "Input.CurrentPassword" : "Input.NewPassword", PasswordSettings.Error(error));
            return InvalidForm();
        }
        await signIn.RefreshSignInAsync(user);
        StatusMessage = "Geslo je bilo uspešno spremenjeno.";
        return RedirectToPage();
    }

    private PageResult InvalidForm()
    {
        Input = new();
        foreach (var key in ModelState.Keys.ToArray()) ModelState.SetModelValue(key, null, null);
        return Page();
    }
}
