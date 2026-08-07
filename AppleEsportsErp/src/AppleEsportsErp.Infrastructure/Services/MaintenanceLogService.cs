using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.PcManagement;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

public interface IMaintenanceLogService
{
    Task LogMaintenanceAsync(Guid pcId, Guid branchId, Guid actorId, string actorRole, string reason);
    Task ResolveMaintenanceAsync(Guid maintenanceLogId, Guid actorId, string actorRole, string? resolutionNotes);
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

    // Marking/resolving maintenance is open to Operators as well as Admin/SuperAdmin, so the
    // actor id may live in either the Operators or Users table — resolve by role, not by a fixed FK.
    private async Task<string> ResolveActorNameAsync(Guid actorId, string actorRole)
    {
        if (actorRole == Roles.Operator)
        {
            var op = await _db.Operators.AsNoTracking().FirstOrDefaultAsync(o => o.Id == actorId);
            return op?.FullName ?? "Unknown";
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId);
        return user?.FullName ?? "Unknown";
    }

    public async Task LogMaintenanceAsync(Guid pcId, Guid branchId, Guid actorId, string actorRole, string reason)
    {
        try
        {
            var log = new MaintenanceLog
            {
                Id = Guid.NewGuid(),
                PcId = pcId,
                BranchId = branchId,
                OperatorId = actorId,
                ActorRole = actorRole,
                MarkedByName = await ResolveActorNameAsync(actorId, actorRole),
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

    public async Task ResolveMaintenanceAsync(Guid maintenanceLogId, Guid actorId, string actorRole, string? resolutionNotes)
    {
        try
        {
            var log = await _db.MaintenanceLogs.FirstOrDefaultAsync(m => m.Id == maintenanceLogId);
            if (log == null)
                throw new InvalidOperationException("Maintenance log not found");

            log.ResolvedAt = DateTimeOffset.UtcNow;
            log.ResolutionNotes = resolutionNotes;
            log.ResolvedByName = await ResolveActorNameAsync(actorId, actorRole);
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
            .Where(m => m.BranchId == branchId && m.MarkedAt >= cutoffDate)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();

        return logs.Select(MapToDto);
    }

    public async Task<IEnumerable<MaintenanceLogDto>> GetPcMaintenanceHistoryAsync(Guid pcId)
    {
        var logs = await _db.MaintenanceLogs
            .AsNoTracking()
            .Include(m => m.Pc)
            .Where(m => m.PcId == pcId)
            .OrderByDescending(m => m.MarkedAt)
            .ToListAsync();

        return logs.Select(MapToDto);
    }

    public async Task<MaintenanceLogDto?> GetActiveMaintenanceAsync(Guid pcId)
    {
        var log = await _db.MaintenanceLogs
            .AsNoTracking()
            .Include(m => m.Pc)
            .FirstOrDefaultAsync(m => m.PcId == pcId && !m.IsResolved);

        return log == null ? null : MapToDto(log);
    }

    private static MaintenanceLogDto MapToDto(MaintenanceLog m) => new()
    {
        Id = m.Id,
        PcId = m.PcId,
        PcName = m.Pc.PcNumber,
        OperatorId = m.OperatorId,
        OperatorName = m.MarkedByName,
        ResolvedByName = m.ResolvedByName,
        Reason = m.Reason,
        MarkedAt = m.MarkedAt,
        ResolvedAt = m.ResolvedAt,
        ResolutionNotes = m.ResolutionNotes,
        IsResolved = m.IsResolved
    };
}
