using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public static class AchievementReconciliationCommand
{
    public const string CommandName = "reconcile-achievements";

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Count == 1 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AchievementReconciliationCommand");

        try
        {
            logger.LogInformation("Starting achievement reconciliation pre-deploy command.");
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Starting database migration.");
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migration completed successfully.");

            logger.LogInformation("Starting historical achievement reconciliation.");
            var runner = scope.ServiceProvider.GetRequiredService<IAchievementReconciliationRunner>();
            var result = await runner.RunAsync(cancellationToken);
            return result.ExitCode;
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            logger.LogCritical(exception, "Achievement reconciliation command failed before completion.");
            return 1;
        }
    }
}
