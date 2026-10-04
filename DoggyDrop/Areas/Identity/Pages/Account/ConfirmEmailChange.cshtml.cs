using System.Text;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Areas.Identity.Pages.Account;

[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ConfirmEmailChangeModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    ApplicationDbContext db) : PageModel
{
    public string StatusMessage { get; private set; } = "Spremembe e-poštnega naslova ni bilo mogoče potrditi.";
    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code)) return Page();
        try { code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return Page(); }
        var user = await users.FindByIdAsync(userId);
        if (user == null) return Page();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var existing = await users.FindByEmailAsync(email);
            if (existing != null && existing.Id != user.Id) return Page();
            // Identity validates the token's user, purpose, target email, lifetime and stamp.
            // Keep both writes atomic: a username uniqueness/concurrency failure must not
            // leave an already-confirmed conflicting email behind.
            if (!(await users.ChangeEmailAsync(user, email, code)).Succeeded ||
                !(await users.SetUserNameAsync(user, email)).Succeeded) return Page();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException) { return Page(); }
        await signIn.RefreshSignInAsync(user); // Framework refreshes only a matching current principal.
        StatusMessage = "E-poštni naslov je potrjen in spremenjen.";
        return Page();
    }
}
