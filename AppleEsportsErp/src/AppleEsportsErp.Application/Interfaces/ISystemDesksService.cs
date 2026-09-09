using System;
using System.Threading.Tasks;
using AppleEsportsErp.Application.DTOs.SystemDesks;

namespace AppleEsportsErp.Application.Interfaces;

public interface ISystemDesksService
{
    Task<OnlineDeskSummaryDto> GetActiveOnlineDeskAsync(Guid branchId, Guid shiftId, DateOnly? fromDate = null, DateOnly? toDate = null);

    /// <summary>Defaults to today's business day when both dates are omitted. A single date
    /// (fromDate only) covers just that business day; both bound a custom range, inclusive.</summary>
    Task<WalletDeskSummaryDto> GetActiveWalletDeskAsync(
        Guid branchId, Guid shiftId, DateOnly? fromDate = null, DateOnly? toDate = null);
}
