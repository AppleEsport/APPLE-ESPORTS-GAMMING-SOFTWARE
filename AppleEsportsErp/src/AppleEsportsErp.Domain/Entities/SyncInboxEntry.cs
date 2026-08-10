namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// An event received at Head Office from a branch, stored exactly as it arrived.
///
/// This is written <b>before</b> anything is interpreted, and that ordering is the whole
/// point. The receiver previously acknowledged batches without storing anything: branches
/// marked their events delivered, Head Office answered 200 OK, and the data evaporated. A
/// branch cannot re-send what it believes was already accepted, so those transactions were
/// gone for good.
///
/// Keeping the raw event means a bug in how an event is interpreted costs nothing permanent —
/// the payload is still here and can be replayed once the interpretation is fixed.
///
/// <see cref="Id"/> is the branch's own outbox entry id, reused deliberately so a re-delivery
/// after a timeout collides on the primary key instead of double-counting a payment.
/// </summary>
public class SyncInboxEntry
{
    /// <summary>The branch's outbox entry id — makes redelivery idempotent.</summary>
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public string AggregateType { get; set; } = null!;
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = null!;

    /// <summary>The original JSON payload, untouched.</summary>
    public string EventData { get; set; } = null!;

    /// <summary>When the branch recorded it (branch clock).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>When Head Office received it. A wide gap means the branch was offline.</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Whether the event has been folded into Head Office's own records. False means it is
    /// safely stored but not yet reflected in reports — for example a session referring to a
    /// PC that Head Office does not know about, which happens until the branch has been
    /// provisioned from Head Office and the two share identifiers.
    /// </summary>
    public bool Applied { get; set; }

    public string? ApplyError { get; set; }

    public Branch? Branch { get; set; }
}
