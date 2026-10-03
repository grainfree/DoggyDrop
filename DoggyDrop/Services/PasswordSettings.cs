using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace DoggyDrop.Services;

public static class PasswordSettings
{
    public const string Policy = "password-settings";

    public static IServiceCollection AddPasswordSettings(this IServiceCollection services)
    {
        services.AddRateLimiter(options => options.AddPolicy(Policy, new PasswordSettingsLimit()));
        return services;
    }

    // Translate known codes only; never render provider/validator descriptions or supplied values.
    public static string Error(IdentityError error) => error.Code switch
    {
        "PasswordMismatch" => "Trenutno geslo ni pravilno.",
        "PasswordTooShort" => "Novo geslo je prekratko.",
        "PasswordRequiresDigit" => "Novo geslo mora vsebovati številko.",
        "PasswordRequiresLower" => "Novo geslo mora vsebovati malo črko.",
        "PasswordRequiresUpper" => "Novo geslo mora vsebovati veliko črko.",
        "PasswordRequiresNonAlphanumeric" => "Novo geslo mora vsebovati poseben znak.",
        "PasswordRequiresUniqueChars" => "Novo geslo mora vsebovati več različnih znakov.",
        _ => "Gesla ni bilo mogoče shraniti. Preveri podatke in poskusi znova."
    };

    private sealed class PasswordSettingsLimit : IRateLimiterPolicy<string>
    {
        public RateLimitPartition<string> GetPartition(HttpContext context) => !HttpMethods.IsPost(context.Request.Method)
            ? RateLimitPartition.GetNoLimiter("read")
            : RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 });

        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => async (context, ct) =>
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.HttpContext.Response.Headers.RetryAfter = "300";
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await context.HttpContext.Response.WriteAsync("Preveč poskusov. Počakaj pet minut, nato se vrni na obrazec in poskusi znova.", ct);
        };
    }
}
