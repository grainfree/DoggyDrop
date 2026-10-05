using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class NotificationDelivery(ApplicationDbContext db, IEmailTransport transport,
    ActivityEmailTemplate templates, TimeProvider clock, ILogger<NotificationDelivery> logger)
{
    public const int BatchSize = 10;
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    public static TimeSpan Backoff(int attempt) => attempt switch {
        1 => TimeSpan.FromMinutes(5), 2 => TimeSpan.FromMinutes(30), 3 => TimeSpan.FromHours(2), _ => TimeSpan.FromHours(12)
    };

    public async Task<NotificationOutbox?> ClaimAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        IQueryable<NotificationOutbox> eligible;
        if (db.Database.IsNpgsql())
            eligible = db.NotificationOutbox.FromSqlInterpolated($"""
                SELECT * FROM "NotificationOutbox"
                WHERE ("Status" = 1 AND "NextAttemptAt" <= {now})
                   OR ("Status" = 2 AND "LeaseUntil" <= {now})
                ORDER BY "NextAttemptAt", "Id" LIMIT 1 FOR UPDATE SKIP LOCKED
                """);
        else
            eligible = db.NotificationOutbox.Where(n => n.Status == EmailDeliveryStatus.Pending && n.NextAttemptAt <= now ||
                n.Status == EmailDeliveryStatus.Processing && n.LeaseUntil <= now).OrderBy(n => n.NextAttemptAt).ThenBy(n => n.Id).Take(1);
        var item = (await eligible.AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        if (item == null) { await transaction.CommitAsync(ct); return null; }
        if (item.AttemptCount >= item.AttemptLimit)
        {
            await db.NotificationOutbox.Where(n => n.Id == item.Id).ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, EmailDeliveryStatus.Failed).SetProperty(n => n.Failure, EmailFailure.AttemptsExhausted)
                .SetProperty(n => n.LeaseToken, (Guid?)null).SetProperty(n => n.LeaseUntil, (DateTime?)null), ct);
            await transaction.CommitAsync(ct);
            return item; // Bounded housekeeping counts toward this poll's batch.
        }
        item.Status = EmailDeliveryStatus.Processing; item.AttemptCount++;
        item.LeaseToken = Guid.NewGuid(); item.LeaseUntil = now + LeaseDuration;
        await db.NotificationOutbox.Where(n => n.Id == item.Id).ExecuteUpdateAsync(s => s
            .SetProperty(n => n.Status, item.Status).SetProperty(n => n.AttemptCount, item.AttemptCount)
            .SetProperty(n => n.LeaseToken, item.LeaseToken).SetProperty(n => n.LeaseUntil, item.LeaseUntil), ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public async Task<int> RunBatchAsync(CancellationToken ct = default)
    {
        var count = 0;
        while (count < BatchSize && !ct.IsCancellationRequested)
        {
            var item = await ClaimAsync(ct);
            if (item == null) break;
            count++;
            await DeliverAsync(item, ct);
        }
        return count;
    }

    public async Task DeliverAsync(NotificationOutbox item, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (item.LeaseToken == null || !await db.NotificationOutbox.AnyAsync(n => n.Id == item.Id &&
            n.Status == EmailDeliveryStatus.Processing && n.LeaseToken == item.LeaseToken && n.LeaseUntil > now, ct)) return;
        var recipient = await db.Users.AsNoTracking().Where(u => u.Id == item.RecipientUserId)
            .Select(u => new { u.Email, u.EmailConfirmed }).SingleOrDefaultAsync(ct);
        if (recipient == null || !recipient.EmailConfirmed || string.IsNullOrWhiteSpace(recipient.Email) ||
            await db.NotificationPreferences.AnyAsync(p => p.UserId == item.RecipientUserId && !p.ContributionUpdates, ct))
        { await CompleteAsync(item, EmailFailure.Suppressed, ct); return; }

        RenderedEmail message;
        try
        {
            var bin = await db.TrashBins.AsNoTracking().Where(b => b.Id == item.BinId)
                .Select(b => new { b.Name, Public = b.IsApproved && !b.IsRetired }).SingleOrDefaultAsync(ct);
            message = templates.Render(item, bin?.Name, bin?.Public == true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { await CompleteAsync(item, EmailFailure.Rendering, ct); return; }
        // Recheck account/claim immediately before handing data to the transport.
        if (!await db.NotificationOutbox.AnyAsync(n => n.Id == item.Id && n.LeaseToken == item.LeaseToken &&
            n.Status == EmailDeliveryStatus.Processing && n.LeaseUntil > clock.GetUtcNow().UtcDateTime, ct)) return;
        recipient = await db.Users.AsNoTracking().Where(u => u.Id == item.RecipientUserId)
            .Select(u => new { u.Email, u.EmailConfirmed }).SingleOrDefaultAsync(ct);
        if (recipient == null || !recipient.EmailConfirmed || string.IsNullOrWhiteSpace(recipient.Email) ||
            await db.NotificationPreferences.AnyAsync(p => p.UserId == item.RecipientUserId && !p.ContributionUpdates, ct))
        { await CompleteAsync(item, EmailFailure.Suppressed, ct); return; }
        EmailFailure outcome;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { outcome = await transport.SendAsync(recipient.Email, message, timeout.Token).WaitAsync(timeout.Token); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { outcome = EmailFailure.Transient; }
        await CompleteAsync(item, outcome, ct);
    }

    private async Task CompleteAsync(NotificationOutbox item, EmailFailure outcome, CancellationToken ct)
    {
        if (!Enum.IsDefined(outcome)) outcome = EmailFailure.Transient;
        var now = clock.GetUtcNow().UtcDateTime;
        var status = outcome == EmailFailure.None ? EmailDeliveryStatus.Sent : outcome == EmailFailure.Suppressed
            ? EmailDeliveryStatus.Suppressed : outcome == EmailFailure.Transient && item.AttemptCount < item.AttemptLimit
                ? EmailDeliveryStatus.Pending : EmailDeliveryStatus.Failed;
        await db.NotificationOutbox.Where(n => n.Id == item.Id && n.Status == EmailDeliveryStatus.Processing && n.LeaseToken == item.LeaseToken)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, status).SetProperty(n => n.Failure, outcome)
                .SetProperty(n => n.SentAt, outcome == EmailFailure.None ? now : (DateTime?)null)
                .SetProperty(n => n.NextAttemptAt, now + Backoff(item.AttemptCount))
                .SetProperty(n => n.LeaseToken, (Guid?)null).SetProperty(n => n.LeaseUntil, (DateTime?)null), ct);
        logger.LogInformation("Activity email {NotificationId}, attempt {Attempt}, outcome {Outcome}", item.Id, item.AttemptCount, outcome);
    }

    public static Task<int> RetryAsync(ApplicationDbContext db, long id, DateTime now, CancellationToken ct = default) =>
        db.NotificationOutbox.Where(n => n.Id == id && n.Status == EmailDeliveryStatus.Failed && n.AttemptCount < 10)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, EmailDeliveryStatus.Pending)
                .SetProperty(n => n.AttemptLimit, n => Math.Max(5, n.AttemptCount + 1))
                .SetProperty(n => n.NextAttemptAt, now), ct);
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment environment,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsProduction() || !bool.TryParse(configuration["Notifications:DeliveryEnabled"], out var enabled) || !enabled) return;
        // Delay first poll: startup/health never waits for SMTP or outbox access.
        using var timer = new PeriodicTimer(NotificationDelivery.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<NotificationDelivery>().RunBatchAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogWarning("Activity email worker failed; next poll will retry safely."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
