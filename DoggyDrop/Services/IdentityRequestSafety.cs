using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DoggyDrop.Services;

public static class IdentityRequestSafety
{
    public static IServiceCollection AddIdentityRequestSafety(this IServiceCollection services)
    {
        services.AddSingleton<IdentityEmailBudget>();
        services.AddScoped<IdentityEmailSafetyFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<IdentityEmailSafetyFilter>());
        return services;
    }
}

// Shared across the existing registration/recovery/verification mail paths, including
// Identity UI library pages. No new email or authentication system is introduced.
public sealed class IdentityEmailBudget : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter = PartitionedRateLimiter.Create<string, string>(
        peer => RateLimitPartition.GetFixedWindowLimiter(peer, _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(15), QueueLimit = 0, AutoReplenishment = true
        }));
    public RateLimitLease Acquire(HttpContext context) => limiter.AttemptAcquire(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown-peer");
    public void Dispose() => limiter.Dispose();
}

public sealed class IdentityEmailSafetyFilter(IdentityEmailBudget budget, IConfiguration configuration,
    IWebHostEnvironment environment) : IAsyncPageFilter
{
    private static readonly HashSet<string> EmailPages = new(StringComparer.OrdinalIgnoreCase) {
        "/Account/Register", "/Account/ForgotPassword", "/Account/ResendEmailConfirmation", "/Account/Manage/Email"
    };
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;
    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (context.ActionDescriptor.AreaName != "Identity")
        { await next(); return; }

        // Reset/confirmation URLs carry bearer tokens. Keep them out of referrers
        // and caches; the shared layout must not weaken this with a meta policy.
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!EmailPages.Contains(context.ActionDescriptor.ViewEnginePath))
        { await next(); return; }

        if (HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            using var lease = budget.Acquire(context.HttpContext);
            if (!lease.IsAcquired)
            {
                context.HttpContext.Response.Headers.RetryAfter = "900";
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                context.Result = new ContentResult { StatusCode = 429, ContentType = "text/plain; charset=utf-8",
                    Content = "Preveč zahtev za e-pošto. Počakaj petnajst minut in poskusi znova." };
                return;
            }
        }
        // Override only absolute link generation, not the request host/scheme or cookies.
        // Reuse the existing validated canonical public origin; no host/forwarded header
        // can choose the recipient of a reset/confirmation token.
        if (context.HandlerInstance is PageModel page)
            page.Url = new TrustedEmailUrls(page.Url, new SeoSite(configuration, environment).Origin);
        await next();
    }
    private sealed class TrustedEmailUrls(IUrlHelper inner, Uri origin) : IUrlHelper
    {
        public ActionContext ActionContext => inner.ActionContext;
        public string? Content(string? path) => inner.Content(path);
        public bool IsLocalUrl(string? url) => inner.IsLocalUrl(url);
        public string? Action(UrlActionContext context)
        {
            if (context.Protocol != null || context.Host != null) { context.Protocol = origin.Scheme; context.Host = origin.Authority; }
            return inner.Action(context);
        }
        public string? RouteUrl(UrlRouteContext context)
        {
            if (context.Protocol != null || context.Host != null) { context.Protocol = origin.Scheme; context.Host = origin.Authority; }
            return inner.RouteUrl(context);
        }
        public string? Link(string? routeName, object? values) => inner.RouteUrl(new UrlRouteContext {
            RouteName = routeName, Values = values, Protocol = origin.Scheme, Host = origin.Authority
        });
    }
}
