using System.Threading.RateLimiting;

namespace DoggyDrop.Services;

public static class WalkingRoutingRegistration
{
    public static IServiceCollection AddWalkingRouting(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<WalkingRouteBudget>();
        services.AddHttpClient<IWalkingRoutes, OrsWalkingRoutes>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DoggyDrop/1.0 (https://doggydrop.app)");
        }).RemoveAllLoggers();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("walking-route", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }
}
