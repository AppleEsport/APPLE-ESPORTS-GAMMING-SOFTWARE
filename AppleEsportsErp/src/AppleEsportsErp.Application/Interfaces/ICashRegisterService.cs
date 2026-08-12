using AppleEsportsErp.Application.DTOs.Cash;

namespace AppleEsportsErp.Application.Interfaces;

public interface ICashRegisterService
{
    Task<CashRegisterDto> GetActiveRegisterAsync(Guid branchId, Guid shiftId);

    /// <summary>
    /// Which of the two opening questions to ask, and the answer to the second one.
    ///
    /// A branch has one drawer and it runs through the trading day. Only the first shift of the
    /// day puts money in it; every later shift inherits what the last one left. Asking a later
    /// shift for a float and then discarding what they type — which is what the server does,
    /// correctly — leaves an operator typing a number that quietly means nothing.
    /// </summary>
    Task<RegisterOpeningDto> GetOpeningAsync(Guid branchId);

    Task<CashRegisterDto> OpenRegisterAsync(Guid branchId, Guid operatorId, Guid shiftId, OpenRegisterDto dto);
    Task<CashRegisterDto> AddTransactionAsync(Guid branchId, Guid operatorId, Guid shiftId, AddCashTransactionDto dto);
}
