using System.ComponentModel.DataAnnotations;

namespace AppleEsportsErp.Application.DTOs.Shift;

/// <summary>
/// A shift somebody left open at this branch, waiting to be closed by whoever came in next.
///
/// Note what is <b>not</b> here at the counting stage: how much the system thinks is in the
/// drawer, and how much stock it thinks there is. The incoming operator counts what they
/// actually find first, and only then are they shown the comparison. Sending the expected
/// figures up front would turn the count into a tick-box, and a tick-box is how a shortfall
/// silently becomes the wrong person's fault.
/// </summary>
public class PendingTakeoverDto
{
    /// <summary>"count" — nothing counted yet. "reason" — counted, and the difference needs explaining.</summary>
    public string Stage { get; set; } = TakeoverStages.Count;

    public Guid OutgoingShiftId { get; set; }
    public string OutgoingOperatorName { get; set; } = null!;

    /// <summary>When the abandoned shift began.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>The last thing that happened on it. Not the same as when it started.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    public int UnattendedMinutes { get; set; }

    /// <summary>
    /// Other shifts at this branch that will be closed by the same handover. Normally 0. The
    /// drawer is counted once whatever the number, because there is one of it.
    /// </summary>
    public int AlsoClosing { get; set; }

    /// <summary>
    /// False when there is no drawer open to count — a shift opened and abandoned before
    /// anyone put money in. The cash step is skipped rather than asking for a count of nothing.
    /// </summary>
    public bool HasOpenDrawer { get; set; }

    /// <summary>What to count, without the quantities. Blind, for the same reason as the cash.</summary>
    public List<TakeoverStockItemDto> StockItems { get; set; } = new();

    /// <summary>Only filled in at the "reason" stage, once the blind count is on record.</summary>
    public TakeoverComparisonDto? Comparison { get; set; }
}

public static class TakeoverStages
{
    public const string Count = "count";
    public const string Reason = "reason";
}

public class TakeoverStockItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Category { get; set; }
}

/// <summary>What the incoming operator found, submitted before they are shown anything.</summary>
public class SubmitTakeoverCountDto
{
    [Range(0, 10000000, ErrorMessage = "The counted cash cannot be negative.")]
    public decimal CountedCash { get; set; }

    public List<TakeoverStockCountDto> StockCounts { get; set; } = new();
}

public class TakeoverStockCountDto
{
    public Guid InventoryId { get; set; }

    [Range(0, 1000000)]
    public int Counted { get; set; }
}

/// <summary>The count against what was expected. Shown only after the count is recorded.</summary>
public class TakeoverComparisonDto
{
    public decimal ExpectedCash { get; set; }
    public decimal CountedCash { get; set; }

    /// <summary>Counted minus expected. Negative is short, positive is over.</summary>
    public decimal CashDifference { get; set; }

    public List<TakeoverStockDifferenceDto> StockDifferences { get; set; } = new();

    public string OutgoingOperatorName { get; set; } = null!;
}

public class TakeoverStockDifferenceDto
{
    public Guid InventoryId { get; set; }
    public string ItemName { get; set; } = null!;
    public int Expected { get; set; }
    public int Counted { get; set; }
    public int Difference { get; set; }
}

/// <summary>Outcome of submitting the count.</summary>
public class TakeoverCountResultDto
{
    /// <summary>
    /// True when everything agreed and there was nothing to explain — the handover finished in
    /// one step and the incoming shift has started.
    /// </summary>
    public bool Completed { get; set; }

    /// <summary>The incoming operator's new shift. Only set once the handover is complete.</summary>
    public Guid? ShiftId { get; set; }

    /// <summary>What was expected against what was found. Always returned, difference or not.</summary>
    public TakeoverComparisonDto Comparison { get; set; } = null!;
}

/// <summary>The explanation for a difference, which finishes the handover.</summary>
public class ConfirmTakeoverDto
{
    [Required(ErrorMessage = "Say what you think happened to the difference.")]
    public string Reason { get; set; } = null!;
}

public class TakeoverCompletedDto
{
    /// <summary>The incoming operator's new shift, now that the previous one is properly closed.</summary>
    public Guid ShiftId { get; set; }
}
