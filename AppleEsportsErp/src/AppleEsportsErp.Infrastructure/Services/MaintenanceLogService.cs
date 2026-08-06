using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.DTOs.PcManagement;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

public interface IMaintenanceLogService
{
    Task LogMaintenanceAsync(Guid pcId, Guid branchId, Guid operatorId, string reason);
    Task ResolveMaintenanceAsync(Guid maintenanceLogId, string? resolutionNotes);
    Task<IEnumerable<MaintenanceLogDto>> GetBranchMaintenanceLogsAsync(Guid branchId, int days = 7);
    Task<IEnumerable<MaintenanceLogDto>> GetPcMaintenanceHistoryAsync(Guid pcId);
    Task<MaintenanceLogDto?> GetActiveMaintenanceAsync(Guid pcId);
}

public class MaintenanceLogService : IMaintenanceLogService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MaintenanceLogService> _logger;

    public MaintenanceLogService(AppDbContext db, ILogger<MaintenanceLogService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogMaintenanceAsync(Guid pcId, Guid branchId, Guid operatorId, string reason)
    {
        try
        {
            var log = new MaintenanceLog
            {
                Id = Guid.NewGuid(),
                PcId = pcId,
                BranchId = branchId,
                OperatorId = operatorId,
                Reason = reason,
                MarkedAt = DateTimeOffset.UtcNow,
                IsResolved = false
            };

            _db.MaintenanceLogs.Add(log);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Maintenance logged for PC {PcId}: {Reason}", pcId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log maintenance for PC {PcId}", pcId);
            throw;
        }
    }

    public async Task ResolveMaintenanceAsync(Guid maintenanceLogId, string? resolutionNotes)
    {
        try
        {
            var log = await _db.MaintenanceLogs.FirstOrDefaultAsync(m => m.Id == maintenanceLogId);
            if (log == null)
                throw new InvalidOperationException("Maintenance log not found");

            log.ResolvedAt = DateTimeOffset.UtcNow;
            log.ResolutionNotes = resolutionNotes;
            log.IsResolved = true;

            _db.MaintenanceLogs.Update(log);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Maintenance resolved for PC {PcId}", log.PcId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve maintenance {MaintenanceLogId}", maintenanceLogId);
            throw;
        }
    }

    public async Task<IEnumerable<MaintenanceLogDto>> GetBranchMaintenanceLogsAsync(Guid branchId, int days = 7)
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-days);

        var logs = await _db.MaintenanceLogs
            .AsNoTracking()
            .Include(m => m.Pc)
            .Include(m => m.Operator)
            .Where(m => m.BranchId == branchId && m.MarkedAt >= cutoffDate)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();

        return logs.Select(m => new MaintenanceLogDto
        {
            Id = m.Id,
            PcId = m.PcId,
            PcName = m.Pc.PcNumber,
            OperatorId = m.OperatorId,
            OperatorName = m.Operator.FullName,
            Reason = m.Reason,
            MarkedAt = m.MarkedAt,
            ResolvedAt = m.ResolvedAt,
            ResolutionNotes = m.ResolutionNotes,
            IsResolved = m.IsResolved
        });
    }

    public async Task<IEnumerable<MaintenanceLogDto>> GetPcMaintenanceHistoryAsync(Guid pcId)
    {
        var logs = await _db.MaintenanceLogs
            .AsNoTracking()
            .Include(m => m.Pc)
            .Include(m => m.Operator)
            .Where(m => m.PcId == pcId)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();

        return logs.Select(m => new MaintenanceLogDto
        {
            Id = m.Id,
            PcId = m.PcId,
            PcName = m.Pc.PcNumber,
            OperatorId = m.OperatorId,
            OperatorName = m.Operator.FullName,
            Reason = m.Reason,
            MarkedAt = m.MarkedAt,
            ResolvedAt = m.ResolvedAt,
            ResolutionNotes = m.ResolutionNotes,
            IsResolved = m.IsResolved
        });
    }

    public async Task<MaintenanceLogDto?> GetActiveMaintenanceAsync(Guid pcId)
    {
        var log = await _db.MaintenanceLogs
            .AsNoTracking()
            .Include(m => m.Pc)
            .Include(m => m.Operator)
            .FirstOrDefaultAsync(m => m.PcId == pcId && !m.IsResolved);

        if (log == null)
            return null;

        return new MaintenanceLogDto
        {
            Id = log.Id,
            PcId = log.PcId,
            PcName = log.Pc.PcNumber,
            OperatorId = log.OperatorId,
            OperatorName = log.Operator.FullName,
            Reason = log.Reason,
            MarkedAt = log.MarkedAt,
            ResolvedAt = log.ResolvedAt,
            ResolutionNotes = log.ResolutionNotes,
            IsResolved = log.IsResolved
        };
    }
}
