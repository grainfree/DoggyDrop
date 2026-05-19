using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DoggyDrop.Areas.Identity.Pages.Account;

public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public ExternalLoginModel(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = "/";

    public string ProviderDisplayName { get; set; } = "Google";

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "Email naslov je obvezen.")]
        [EmailAddress(ErrorMessage = "Vnesi veljaven email naslov.")]
        public string Email { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ErrorMessage = $"Napaka pri zunanji prijavi: {remoteError}";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "Napaka pri pridobivanju podatkov o zunanji prijavi.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        var signInResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (signInResult.Succeeded)
        {
            return LocalRedirect(returnUrl);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            var existingUser = await _userManager.FindByEmailAsync(email);
            if (existingUser != null)
            {
                var existingLogins = await _userManager.GetLoginsAsync(existingUser);
                if (!existingLogins.Any(login => login.LoginProvider == info.LoginProvider && login.ProviderKey == info.ProviderKey))
                {
                    var linkResult = await _userManager.AddLoginAsync(existingUser, info);
                    if (!linkResult.Succeeded)
                    {
                        ErrorMessage = "Google prijave ni bilo mogoče povezati z obstoječim računom.";
                        return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
                    }
                }

                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            var createdUser = await CreateExternalUserAsync(info, email);
            if (createdUser != null)
            {
                await _signInManager.SignInAsync(createdUser, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            Input.Email = email;
        }

        ProviderDisplayName = info.ProviderDisplayName ?? "Google";
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            ErrorMessage = "Napaka pri potrditvi zunanjih informacij.";
            return RedirectToPage("./Login", new { ReturnUrl = returnUrl });
        }

        if (!ModelState.IsValid)
        {
            ProviderDisplayName = info.ProviderDisplayName ?? "Google";
            ReturnUrl = returnUrl;
            return Page();
        }

        var existingUser = await _userManager.FindByEmailAsync(Input.Email);
        if (existingUser != null)
        {
            var linkResult = await _userManager.AddLoginAsync(existingUser, info);
            if (linkResult.Succeeded)
            {
                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            foreach (var error in linkResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        else
        {
            var createdUser = await CreateExternalUserAsync(info, Input.Email);
            if (createdUser != null)
            {
                await _signInManager.SignInAsync(createdUser, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            ModelState.AddModelError(string.Empty, "Google računa ni bilo mogoče dokončati.");
        }

        ProviderDisplayName = info.ProviderDisplayName ?? "Google";
        ReturnUrl = returnUrl;
        return Page();
    }

    private async Task<ApplicationUser?> CreateExternalUserAsync(ExternalLoginInfo info, string email)
    {
        var displayName = info.Principal.FindFirstValue("name")
            ?? info.Principal.FindFirstValue(ClaimTypes.Name)
            ?? email.Split('@')[0];

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            return null;
        }

        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            return null;
        }

        return user;
    }
}
