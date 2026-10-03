using System.Net.Mail;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public enum AdminBootstrapOutcome { NotConfigured, ExistingAdministrator, Created }

/// <summary>Initial account creation only. Never a password-reset or account-promotion path.</summary>
public static class AdminBootstrap
{
    public const string EmailKey = "AdminBootstrap:Email";
    public const string PasswordKey = "AdminBootstrap:Password";
    private const string RoleName = "Admin";

    public static async Task<AdminBootstrapOutcome> RunAsync(IServiceProvider services, IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminBootstrap));
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            // Multiple application instances must not independently bootstrap administrators.
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(194721, 23001)", cancellationToken);

            var role = await roles.FindByNameAsync(RoleName);
            if (role == null)
            {
                role = new IdentityRole(RoleName);
                RequireSuccess(await roles.CreateAsync(role));
            }

            if (await db.UserRoles.AnyAsync(link => link.RoleId == role.Id, cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation("Administrator already exists; initial bootstrap skipped.");
                return AdminBootstrapOutcome.ExistingAdministrator;
            }

            var email = configuration[EmailKey]?.Trim();
            var password = configuration[PasswordKey];
            if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            {
                await transaction.CommitAsync(cancellationToken);
                logger.LogWarning("Administrator bootstrap skipped: no administrator or bootstrap configuration. Supply AdminBootstrap configuration explicitly if initial setup is required.");
                return AdminBootstrapOutcome.NotConfigured;
            }

            // No environment has a default credential. Identity's password validators also apply.
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || password.Length < 12 ||
                !MailAddress.TryCreate(email, out var address) || !string.Equals(address.Address, email, StringComparison.Ordinal))
                throw new InvalidOperationException();

            // Configuration must never elevate an existing ordinary account or change its password.
            if (await users.FindByEmailAsync(email) != null || await users.FindByNameAsync(email) != null)
                throw new InvalidOperationException();

            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            RequireSuccess(await users.CreateAsync(user, password));
            RequireSuccess(await users.AddToRoleAsync(user, RoleName));
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Initial administrator created from explicit bootstrap configuration.");
            return AdminBootstrapOutcome.Created;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Identity validators/providers may include supplied values in errors. Do not propagate
            // their descriptions or inner exceptions to startup logs. Never log configuration values.
            throw new InvalidOperationException("Administrator bootstrap failed. Check AdminBootstrap:Email and AdminBootstrap:Password, password requirements, account conflicts and database availability. Existing accounts are not promoted or reset.");
        }
    }

    private static void RequireSuccess(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException();
    }
}
