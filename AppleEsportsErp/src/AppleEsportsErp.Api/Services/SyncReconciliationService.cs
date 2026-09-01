using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// The self-healing half of sync.
///
/// Everything else in this system captures a row once, at the moment it changes, and trusts
/// that single capture to eventually reach Head Office (see SyncCapture). That is right for a
/// bill or a login - each happens once, so the capture and the delivery attempt it creates are
/// the same event. Shift and CashRegister rows are different: they stay open for hours, and
/// nothing else ever touches them again while they do. A single missed capture - a stale JWT
/// claim attaching a new cash register to the wrong shift, a bug not yet found, a crash before
/// the save that would have captured it - had no second chance, because nothing about an open
/// shift ever changes again to give the capture path another try. That is exactly how a real
/// cash register opened at Citylight sat with zero delivery attempts, forever, until someone
/// opened the database by hand and asked why its opening balance never reached the server.
///
/// This asks a much simpler, much more robust question instead of trying to catch every way a
/// capture can be missed: for every row that is still genuinely open right now, is a delivery
/// attempt already sitting undelivered in the outbox? If not, queue a fresh one. It does not
/// need to know why the first attempt is missing - it just guarantees a still-open row is never
/// more than one sweep away from another try. SyncInboxController applies these as an upsert
/// keyed on the row's own id, so re-queuing a row that actually arrived fine already is a no-op
/// at the other end, not a duplicate.
///
/// Branch-only, like PcAgentWatchdogService: Head Office's own copy of these rows is what
/// branches sync TO, not a source to sync FROM - see BranchOnlyBackgroundService.
/// </summary>
public class SyncReconciliationService : BranchOnlyBackgroundService
{
    /// <summary>
    /// Fifteen minutes: frequent enough that a missed capture is never stuck for long, cheap
    /// enough that re-checking a handful of open shifts and registers costs nothing between
    /// sweeps.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<SyncReconciliationService> _logger;

    public SyncReconciliationService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<SyncReconciliationService> logger)
        : base(configuration, logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncReconciliationService is starting.");

        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await ResweepAsync(db, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed while re-checking still-open rows for undelivered sync.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("SyncReconciliationService is stopping.");
    }

    private async Task ResweepAsync(AppDbContext db, CancellationToken ct)
    {
        var queued = 0;

        var openShifts = await db.Shifts
            .Where(s => s.Status == ShiftStatus.Active)
            .ToListAsync(ct);
        foreach (var shift in openShifts)
            queued += await RequeueIfNeededAsync(db, shift, ct);

        var openRegisters = await db.CashRegisters
            .Where(r => r.Status != CashRegisterStatus.Closed)
            .ToListAsync(ct);
        foreach (var register in openRegisters)
            queued += await RequeueIfNeededAsync(db, register, ct);

        // Lowercase to match the literal BillingService/SessionService actually write - there
        // is no CreditStatus enum backing this column, just this string.
        var pendingCredits = await db.CustomerCredits
            .Where(c => c.Status == "pending")
            .ToListAsync(ct);
        foreach (var credit in pendingCredits)
            queued += await RequeueIfNeededAsync(db, credit, ct);

        if (queued > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Sync reconciliation: {Count} still-open row(s) had no delivery attempt waiting and were re-queued.",
                queued);
        }
    }

    private static async Task<int> RequeueIfNeededAsync<TEntity>(AppDbContext db, TEntity entity, CancellationToken ct)
        where TEntity : class
    {
        var entry = SyncCapture.BuildEntryFor(db.Entry(entity));
        if (entry is null) return 0;

        // Never piles up behind a delivery attempt the courier simply hasn't gotten to yet -
        // this runs every fifteen minutes and must not flood the outbox with competing copies
        // of the exact same row.
        var alreadyQueued = await db.SyncOutboxEntries.AnyAsync(
            e => e.AggregateId == entry.AggregateId
                && e.AggregateType == entry.AggregateType
                && e.SyncedAt == null,
            ct);
        if (alreadyQueued) return 0;

        db.SyncOutboxEntries.Add(entry);
        return 1;
    }
}
