using AppleEsportsErp.Application.DTOs.Eod;

namespace AppleEsportsErp.Application.Interfaces;

public interface IEodService
{
    Task<EodReportDto> GenerateEodReportAsync(Guid branchId, DateTimeOffset targetDate);
}
