using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// Records "this happened here" so the courier can carry it to Head Office.
///
/// Call this <b>inside the caller's transaction</b>. The business record and its sync entry
/// must commit together or not at all: a session that exists at the branch but never reaches
/// Head Office is precisely the "the four branches don't add up" problem the whole
/// offline-first design exists to prevent.
///
/// For that reason a failure here is deliberately allowed to propagate and roll the caller
/// back. This used to swallow every exception, which quietly broke the guarantee — the
/// session committed, the sync entry vanished, and nobody found out until the month-end
/// figures disagreed. Since the insert goes to the same database and the same transaction as
/// the session itself, a failure here means that write was doomed anyway.
/// </summary>
public class OutboxService : IOutboxService
{
    private readonly AppDbContext _db;
    private readonly ILogger<OutboxService> _logger;

    public OutboxService(AppDbContext db, ILogger<OutboxService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordEventAsync(
        Guid branchId,
        string aggregateType,
        Guid aggregateId,
        string eventType,
        object eventData)
    {
        var entry = new SyncOutboxEntry
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = eventType,
            EventData = JsonSerializer.Serialize(eventData),
            CreatedAt = DateTime.UtcNow,
            SyncedAt = null,
            AttemptCount = 0
        };

        _db.SyncOutboxEntries.Add(entry);
        await _db.SaveChangesAsync();

        _logger.LogDebug("Recorded sync outbox entry: {AggregateType} {AggregateId} - {EventType} for branch {BranchId}",
            aggregateType, aggregateId, eventType, branchId);
    }
}
