using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public static class DatabaseMigrationCommand
{
    public const string CommandName = "migrate";

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Count == 1 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseMigrationCommand");

        try
        {
            await using var scope = services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            logger.LogInformation("Starting database migration.");
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migration completed successfully.");
            return 0;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogCritical("Database migration failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            return 1;
        }
    }
}
