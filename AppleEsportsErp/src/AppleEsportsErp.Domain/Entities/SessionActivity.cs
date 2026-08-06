namespace AppleEsportsErp.Domain.Entities;

public class SessionActivity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid BranchId { get; set; }

    public string Action { get; set; } = null!; // "session_started", "session_stopped", "credit_added", etc.
    public string Description { get; set; } = null!; // Human-readable message
    public decimal? Amount { get; set; } // For transactions (credits, food, billing)

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public Session Session { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
