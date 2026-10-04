using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Areas.Identity.Pages.Account;

[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ExternalLoginModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users,
    ApplicationDbContext db) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string ReturnUrl { get; set; } = "/";
    public string ProviderDisplayName { get; set; } = "Zunanja prijava";
    public bool EmailConflict { get; set; }
    [TempData] public string? ErrorMessage { get; set; }
    public class InputModel
    {
        [Required(ErrorMessage = "E-poštni naslov je obvezen.")]
        [EmailAddress(ErrorMessage = "Vnesi veljaven e-poštni naslov.")]
        public string Email { get; set; } = "";
    }
    private string LocalReturn(string? value) => Url.IsLocalUrl(value) ? value! : "/";
    public async Task<IActionResult> OnPostAsync(string provider, string? returnUrl = null)
    {
        ReturnUrl = LocalReturn(returnUrl);
        if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == provider))
            return Failure();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var redirect = Url.Page("./ExternalLogin", "Callback", new { returnUrl = ReturnUrl });
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(provider, redirect), provider);
    }
    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        ReturnUrl = LocalReturn(returnUrl);
        if (!string.IsNullOrEmpty(remoteError)) return Failure();
        return await CompleteAsync(false);
    }
    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        ReturnUrl = LocalReturn(returnUrl);
        return await CompleteAsync(true);
    }
    private async Task<IActionResult> CompleteAsync(bool submitted)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (info == null || info.AuthenticationProperties?.Items.ContainsKey("XsrfId") == true)
            return Failure(); // An account-management challenge is not a registration/login challenge.
        ProviderDisplayName = info.ProviderDisplayName ?? "Zunanja prijava";
        // A failed linked login must never fall through to email lookup/registration.
        var linked = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linked != null)
        {
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, bypassTwoFactor: false);
            if (result.Succeeded) return LocalRedirect(ReturnUrl);
            if (result.RequiresTwoFactor) return RedirectToPage("./LoginWith2fa", new { ReturnUrl, RememberMe = false });
            if (result.IsLockedOut) return RedirectToPage("./Lockout");
            return Failure();
        }
        var claimedEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
        // Provider email, when present, cannot be replaced by a posted form value.
        var email = string.IsNullOrWhiteSpace(claimedEmail) ? (submitted ? Input.Email?.Trim() : null) : claimedEmail.Trim();
        ModelState.Clear();
        Input.Email = email ?? "";
        if (email == null && !submitted) return Page();
        if (!TryValidateModel(Input, nameof(Input))) return Page();
        email = Input.Email;
        if (await users.FindByEmailAsync(email) != null || await users.FindByNameAsync(email) != null)
        {
            EmailConflict = true;
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Page();
        }
        // The unique normalized username (email) and provider tuple remain DB-enforced.
        // A losing registration never retries by attaching to the winning account.
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = new ApplicationUser {
                UserName = email, Email = email, EmailConfirmed = false,
                DisplayName = info.Principal.FindFirstValue("name") ?? info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Uporabnik"
            };
            if (!(await users.CreateAsync(user)).Succeeded || !(await users.AddLoginAsync(user, info)).Succeeded)
                return RegistrationFailure();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return RegistrationFailure(); }
        // Respect the same Identity sign-in policy as an already-linked account.
        var createdResult = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, false, bypassTwoFactor: false);
        return createdResult.Succeeded ? LocalRedirect(ReturnUrl) : Failure();
    }
    private IActionResult RegistrationFailure()
    {
        ModelState.AddModelError("", "Računa ni bilo mogoče ustvariti. Poskusi znova ali se prijavi v obstoječi račun.");
        return Page();
    }
    private IActionResult Failure()
    {
        ErrorMessage = "Zunanje prijave ni bilo mogoče dokončati. Poskusi znova.";
        return RedirectToPage("./Login", new { ReturnUrl });
    }
}
