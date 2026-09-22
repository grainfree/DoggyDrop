using System.Diagnostics;
using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public interface IAchievementReconciliationRunner
{
    Task<AchievementReconciliationResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class RecoverableAchievementReconciliationException : Exception
{
    public RecoverableAchievementReconciliationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class AchievementReconciliationRunner : IAchievementReconciliationRunner
{
    private const int BatchSize = 100;

    private readonly ApplicationDbContext _context;
    private readonly IUserAchievementService _achievementService;
    private readonly ILogger<AchievementReconciliationRunner> _logger;

    public AchievementReconciliationRunner(
        ApplicationDbContext context,
        IUserAchievementService achievementService,
        ILogger<AchievementReconciliationRunner> logger)
    {
        _context = context;
        _achievementService = achievementService;
        _logger = logger;
    }

    public async Task<AchievementReconciliationResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        // Freeze the highest ID present at command start. Keyset paging then cannot
        // skip an initial user when concurrent registrations shift table positions.
        var populationUpperBound = await _context.Users.AsNoTracking()
            .OrderByDescending(user => user.Id)
            .Select(user => user.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var usersDiscovered = populationUpperBound == null
            ? 0
            : await _context.Users.AsNoTracking()
                .CountAsync(user => string.Compare(user.Id, populationUpperBound) <= 0, cancellationToken);
        var usersProcessed = 0;
        var usersSucceeded = 0;
        var usersFailed = 0;
        var achievementsInserted = 0;
        var achievementsAlreadyOwned = 0;
        var systemicFailure = false;
        string? lastProcessedUserId = null;

        _logger.LogInformation(
            "Achievement reconciliation initially discovered {UsersDiscovered} users.",
            usersDiscovered);

        while (populationUpperBound != null)
        {
            var query = _context.Users.AsNoTracking()
                .Where(user => string.Compare(user.Id, populationUpperBound) <= 0);
            if (lastProcessedUserId != null)
            {
                query = query.Where(user => string.Compare(user.Id, lastProcessedUserId) > 0);
            }

            var userIds = await query
                .OrderBy(user => user.Id)
                .Select(user => user.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (userIds.Count == 0)
            {
                break;
            }

            foreach (var userId in userIds)
            {
                usersProcessed++;
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var results = await _achievementService.ReconcileUserAsync(userId);
                    await transaction.CommitAsync(cancellationToken);

                    usersSucceeded++;
                    achievementsInserted += results.Count(result => result.NewlyUnlocked);
                    achievementsAlreadyOwned += results.Count(result => !result.NewlyUnlocked);
                }
                catch (RecoverableAchievementReconciliationException exception)
                {
                    var rollbackSucceeded = await RollbackSafelyAsync(transaction, cancellationToken);
                    _context.ChangeTracker.Clear();
                    usersFailed++;
                    _logger.LogError(exception, "Achievement reconciliation failed for user {UserId}.", userId);

                    if (!rollbackSucceeded)
                    {
                        systemicFailure = true;
                        _logger.LogCritical("Achievement reconciliation stopped because a user transaction could not be rolled back.");
                        break;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    await RollbackSafelyAsync(transaction, cancellationToken);
                    _context.ChangeTracker.Clear();
                    usersFailed++;
                    systemicFailure = true;
                    _logger.LogCritical(
                        exception,
                        "Achievement reconciliation stopped after a systemic failure for user {UserId}.",
                        userId);
                    break;
                }
            }

            if (systemicFailure)
            {
                break;
            }

            lastProcessedUserId = userIds[^1];
        }

        stopwatch.Stop();
        var result = new AchievementReconciliationResult(
            usersDiscovered,
            usersProcessed,
            usersSucceeded,
            usersFailed,
            achievementsInserted,
            achievementsAlreadyOwned,
            stopwatch.Elapsed,
            systemicFailure);

        _logger.LogInformation(
            "Achievement reconciliation finished. Users discovered: {UsersDiscovered}; processed: {UsersProcessed}; succeeded: {UsersSucceeded}; failed: {UsersFailed}; achievements inserted: {AchievementsInserted}; already owned: {AchievementsAlreadyOwned}; elapsed: {Elapsed}.",
            result.UsersDiscovered,
            result.UsersProcessed,
            result.UsersSucceeded,
            result.UsersFailed,
            result.AchievementsInserted,
            result.AchievementsAlreadyOwned,
            result.Elapsed);

        return result;
    }

    private async Task<bool> RollbackSafelyAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
            return true;
        }
        catch (Exception rollbackException) when (rollbackException is not OperationCanceledException and not OutOfMemoryException)
        {
            _logger.LogError(rollbackException, "Achievement reconciliation transaction rollback failed.");
            return false;
        }
    }
}
