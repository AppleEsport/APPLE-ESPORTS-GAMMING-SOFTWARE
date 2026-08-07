namespace AppleEsportsErp.Domain.Entities;

public class SyncOutboxEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public string AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; }
    public string EventData { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAt { get; set; }
    public int AttemptCount { get; set; } = 0;
    public string? LastError { get; set; }
}
