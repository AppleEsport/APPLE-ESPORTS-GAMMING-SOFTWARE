using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

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
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording sync outbox entry for branch {BranchId}", branchId);
            // Don't throw - outbox recording should not block the main transaction
        }
    }
}
