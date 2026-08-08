using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// A period where the branch lost power or lost its link to Head Office.
///
/// Recorded so the owner can see, on the day's EOD and on printed reports, exactly when the
/// shop went dark and when it came back — rather than being left to guess why an evening's
/// takings look thin or why a branch's figures arrived late.
/// </summary>
public class DowntimeEvent
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }

    public DowntimeKind Kind { get; set; }

    /// <summary>When the outage began — for a power cut, the last moment the system was alive.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When service was restored.</summary>
    public DateTimeOffset EndedAt { get; set; }

    public int DurationSeconds { get; set; }

    /// <summary>
    /// Live sessions caught by this outage. Only meaningful for a power cut — an internet
    /// outage interrupts nobody's game.
    /// </summary>
    public int SessionsAffected { get; set; }

    /// <summary>
    /// The trading day this outage is reported under, following the 06:00-06:00 IST boundary,
    /// so a cut at 01:00 appears on the night it actually disrupted rather than the next
    /// morning's report. Stored rather than derived so a report never has to recompute it.
    /// </summary>
    public DateOnly BusinessDay { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Branch Branch { get; set; } = null!;
}
