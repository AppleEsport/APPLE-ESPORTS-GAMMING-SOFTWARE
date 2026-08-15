using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Billing;

namespace AppleEsportsErp.Application.Interfaces;

public interface IBillingService
{
    Task<PaginatedResult<BillDto>> GetActiveBillsAsync(Guid branchId, int page = 1, int pageSize = 50);
    Task<List<BillDto>> GetDeferredBillsAsync(Guid branchId);
    Task<BillDto> GetBillAsync(Guid branchId, Guid id);
    Task<BillDto> GetBillByNumberAsync(Guid branchId, string billNumber);
    /// <summary>
    /// <paramref name="actorRole"/> is recorded on the audit entry. It used to be hardcoded to
    /// SuperAdmin regardless of who applied it, which made the audit trail unable to answer the
    /// one question a disputed discount is ever asked: who authorised this.
    /// </summary>
    Task<BillDto> ApplyDiscountAsync(Guid branchId, Guid actorId, string actorRole, Guid id, ApplyDiscountDto dto);
    Task<BillDto> ProcessPaymentAsync(Guid branchId, Guid operatorId, Guid shiftId, Guid id, ProcessPaymentDto dto);
    Task<BillDto> RemoveBillItemAsync(Guid branchId, Guid operatorId, Guid billId, Guid billItemId);
}
