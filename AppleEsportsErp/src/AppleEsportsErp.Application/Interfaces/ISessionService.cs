using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Sessions;

namespace AppleEsportsErp.Application.Interfaces;

public interface ISessionService
{
    Task<PaginatedResult<SessionDto>> GetActiveSessionsAsync(Guid branchId, int page, int pageSize);
    Task<SessionDto> StartSessionAsync(Guid branchId, Guid operatorId, Guid shiftId, SessionStartDto dto);
    Task<SessionDto> StopSessionAsync(Guid branchId, Guid operatorId, Guid sessionId, bool deferPayment = false);

    /// <summary>
    /// Puts a session held after an outage back to Active, with the customer's unused paid
    /// time intact. The wait between the power returning and this call is credited back too,
    /// so a customer is never billed for the minutes an operator spent finding them.
    /// </summary>
    Task<SessionDto> ResumeSessionAsync(Guid branchId, Guid operatorId, Guid sessionId);
    Task<SessionDto> ExtendSessionAsync(Guid branchId, Guid operatorId, Guid sessionId, SessionExtendDto dto);
    Task<SessionDto> TransferSessionAsync(Guid branchId, Guid operatorId, Guid sessionId, SessionTransferDto dto);
}
