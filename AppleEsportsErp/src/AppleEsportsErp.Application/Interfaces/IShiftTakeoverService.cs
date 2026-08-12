using AppleEsportsErp.Application.DTOs.Shift;

namespace AppleEsportsErp.Application.Interfaces;

/// <summary>
/// An operator taking over a shift that nobody closed.
///
/// The owner's words: "B will close A's shift and count all the things and then B will log in."
/// So this is the gate between logging in and trading, and it is the only thing that opens it.
/// </summary>
public interface IShiftTakeoverService
{
    /// <summary>
    /// The handover this operator has to deal with before they can start, or null if there is
    /// none. Safe to call on every login.
    /// </summary>
    Task<PendingTakeoverDto?> GetPendingAsync(Guid branchId, Guid operatorId, CancellationToken ct = default);

    /// <summary>
    /// Records the blind count and reveals the comparison. Finishes the handover outright when
    /// nothing differs; otherwise the count is on record and a reason is still owed.
    /// </summary>
    Task<TakeoverCountResultDto> SubmitCountAsync(
        Guid branchId, Guid operatorId, SubmitTakeoverCountDto dto, CancellationToken ct = default);

    /// <summary>Attaches the explanation for a difference and finishes the handover.</summary>
    Task<TakeoverCompletedDto> ConfirmAsync(
        Guid branchId, Guid operatorId, ConfirmTakeoverDto dto, CancellationToken ct = default);
}
