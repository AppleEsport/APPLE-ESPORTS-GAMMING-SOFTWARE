using AppleEsportsErp.Application.DTOs.Cash;

namespace AppleEsportsErp.Application.Interfaces;

public interface ICashDeskService
{
    Task StartVerificationAsync(Guid branchId, Guid operatorId, Guid shiftId);
    Task<DenominationCountDto> SubmitDenominationsAsync(Guid branchId, Guid operatorId, Guid shiftId, SubmitDenominationDto dto);
    Task CloseRegisterAsync(Guid branchId, Guid operatorId, Guid shiftId, Guid cashRegisterId, CloseCashRegisterDto dto);
    Task CancelVerificationAsync(Guid branchId, Guid operatorId, Guid shiftId, Guid cashRegisterId);

    /// <summary>
    /// Undoes an accidental "last shift of the day" close - Admin/Super Admin only. Only works
    /// while the closed register is still the branch's most recent one: the moment a new register
    /// opens on top of it, there is no longer a single "the day" to un-close, so this refuses
    /// rather than resurrecting stale history.
    /// </summary>
    Task ReopenLastDayCloseAsync(Guid branchId, Guid adminUserId, string adminName);
}
