namespace AppleEsportsErp.Domain.Enums;

/// <summary>How far through a handover the incoming operator has got.</summary>
public enum ShiftHandoverStatus
{
    /// <summary>
    /// The drawer has been counted and the figure is on record, but the difference has not
    /// been explained yet.
    ///
    /// This state exists so the count cannot be revised after the expected figure is shown.
    /// If the operator could see "the system says Rs 5,240" and then type 5,240, the count
    /// would be a tick-box and a shortfall would quietly become the wrong person's problem.
    /// So the blind count is written first, and only then is the comparison revealed.
    /// </summary>
    AwaitingReason,

    /// <summary>
    /// Finished. The abandoned shift is closed, its drawer is closed with this count against
    /// it, and the incoming operator's own shift has started.
    /// </summary>
    Completed,
}
