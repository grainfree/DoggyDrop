using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Areas.Identity.Pages.Account.Manage;

// Standard Identity account-bound linking, with server-side last-method protection.
[Authorize]
[ValidateAntiForgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalLoginsModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : PageModel
{
    public IList<UserLoginInfo> CurrentLogins { get; set; } = [];
    public IList<AuthenticationScheme> OtherLogins { get; set; } = [];
    public bool ShowRemoveButton { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        CurrentLogins = await users.GetLoginsAsync(user);
        OtherLogins = (await signIn.GetExternalAuthenticationSchemesAsync())
            .Where(s => CurrentLogins.All(l => l.LoginProvider != s.Name)).ToList();
        ShowRemoveButton = await users.HasPasswordAsync(user) || CurrentLogins.Count > 1;
        return Page();
    }
    public async Task<IActionResult> OnPostLinkLoginAsync(string provider)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == provider)) return BadRequest();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        var redirect = Url.Page("./ExternalLogins", "LinkLoginCallback");
        return Challenge(signIn.ConfigureExternalAuthenticationProperties(provider, redirect, user.Id), provider);
    }
    public async Task<IActionResult> OnGetLinkLoginCallbackAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var info = await signIn.GetExternalLoginInfoAsync(user.Id);
        if (info == null) return LinkFailure();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await users.AddLoginAsync(user, info);
            if (!result.Succeeded) return LinkFailure();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return LinkFailure(); }
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        StatusMessage = "Zunanja prijava je povezana s tvojim računom.";
        return RedirectToPage();
    }
    public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
    {
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var logins = await users.GetLoginsAsync(user);
            if (!logins.Any(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey) ||
                (!await users.HasPasswordAsync(user) && logins.Count <= 1))
            {
                StatusMessage = "Ohrani vsaj en način prijave. Pred odstranitvijo lahko nastaviš geslo.";
                return RedirectToPage();
            }
            // Identity's user concurrency stamp plus this transaction rolls back a losing
            // concurrent removal, including the login-row deletion preceding user update.
            if (!(await users.RemoveLoginAsync(user, loginProvider, providerKey)).Succeeded) return LinkFailure();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return LinkFailure(); }
        await signIn.RefreshSignInAsync(user);
        StatusMessage = "Zunanja prijava je odstranjena.";
        return RedirectToPage();
    }
    private IActionResult LinkFailure()
    {
        StatusMessage = "Spremembe zunanje prijave ni bilo mogoče dokončati. Poskusi znova.";
        return RedirectToPage();
    }
}
