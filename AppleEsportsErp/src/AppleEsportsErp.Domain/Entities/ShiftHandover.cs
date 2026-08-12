using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// One operator taking over a shift somebody else never closed: what was expected, what was
/// found, the difference, and who counted it.
///
/// Without a record of its own a discrepancy has nowhere to live. The drawer would show a
/// figure and the shift would show a closure, and nothing would connect the two or say that
/// the person who counted was not the person whose takings these were.
///
/// That separation is the whole point of this table. <see cref="OutgoingOperatorId"/> is whose
/// money it is; <see cref="CountedByOperatorId"/> is who counted it. Collapsing them - which is
/// what happens if the count is simply written onto the drawer and the shift closed - lands a
/// shortfall on the name of somebody who was not in the building.
/// </summary>
public class ShiftHandover
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }

    /// <summary>The shift being closed. Whose takings these are.</summary>
    public Guid OutgoingShiftId { get; set; }

    /// <summary>The operator whose shift it was. Not present, by definition.</summary>
    public Guid OutgoingOperatorId { get; set; }

    /// <summary>The operator who counted the drawer and closed the shift. Deliberately separate.</summary>
    public Guid CountedByOperatorId { get; set; }

    /// <summary>
    /// The shift that started once this handover finished. Null while it is still in progress -
    /// the incoming operator does not get a shift until the money before them is on record.
    /// </summary>
    public Guid? IncomingShiftId { get; set; }

    /// <summary>
    /// The drawer that was counted. Null when there was no open drawer to count, which happens
    /// when a shift was opened and abandoned before anyone put money in.
    /// </summary>
    public Guid? CashRegisterId { get; set; }

    /// <summary>What the system said should be in the drawer.</summary>
    public decimal ExpectedCash { get; set; }

    /// <summary>What the incoming operator actually found, counted before they were shown the above.</summary>
    public decimal CountedCash { get; set; }

    /// <summary>Found minus expected. Negative is short, positive is over; both matter.</summary>
    public decimal CashDifference { get; set; }

    /// <summary>
    /// The incoming operator's explanation for the difference, in their own words. Required
    /// when there is a difference, meaningless when there is not.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>Items whose counted quantity differed from the system's, as JSON. Null when stock agreed.</summary>
    public string? StockDifferences { get; set; }

    /// <summary>How long the abandoned shift had been untouched when it was taken over.</summary>
    public int UnattendedMinutes { get; set; }

    public ShiftHandoverStatus Status { get; set; } = ShiftHandoverStatus.AwaitingReason;

    /// <summary>When the blind count was taken. Before the expected figure was shown.</summary>
    public DateTimeOffset CountedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
