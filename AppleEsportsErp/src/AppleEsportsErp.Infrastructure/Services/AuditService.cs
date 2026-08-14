using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// SOP §22: Immutable Audit Trail — INSERT only, failures never block operations.
/// Maps from audit.js logAudit function.
/// </summary>
public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, ILogger<AuditService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(AuditEntry entry)
    {
        try
        {
            // Filled in here rather than left to every call site, because most of them do not
            // set it. SessionService alone writes four different action types on every single
            // session and never once passes a name - it is not an oversight in four places, it
            // is the normal case, and the Activity Log screen this feeds would otherwise show
            // "at (blank)" for the single busiest source of rows in the whole trail.
            //
            // A snapshot, deliberately, the same as AuthService's own calls already take: a
            // branch renamed next month must not rewrite what this row said at the time, and a
            // branch deleted later must not turn old rows into ones naming nothing at all.
            var branchName = entry.BranchName;
            if (string.IsNullOrWhiteSpace(branchName) && entry.BranchId is { } branchId)
            {
                branchName = await _db.Branches.AsNoTracking()
                    .Where(b => b.Id == branchId)
                    .Select(b => b.Name)
                    .FirstOrDefaultAsync();
            }

            // The same problem, and it is worse: a payment, a wallet top-up and a member being
            // registered all pass the literal word "System" here instead of the operator who
            // was actually standing at the counter - even though every one of them already has
            // that operator's id in OperatorId. "System" is the right word for something a
            // background job did with nobody present (ReservationBackgroundService, an
            // automatic close); it is simply wrong for money someone in the room took, and an
            // activity trail that cannot say who took a payment is not much of a trail.
            //
            // Only replaced when there is an OperatorId to resolve and the caller either left
            // the name blank or used that literal placeholder - a caller that has already gone
            // to the trouble of looking up a real name (UserId path, member logins, and so on)
            // is left alone.
            var userName = entry.UserName;
            if ((string.IsNullOrWhiteSpace(userName) || userName == "System")
                && entry.OperatorId is { } operatorId)
            {
                var resolved = await _db.Operators.AsNoTracking()
                    .Where(o => o.Id == operatorId)
                    .Select(o => o.FullName)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(resolved)) userName = resolved;
            }

            var auditLog = new AuditLog
            {
                UserId = entry.UserId,
                OperatorId = entry.OperatorId,
                UserRole = entry.UserRole,
                UserName = userName,
                Action = entry.Action,
                TargetType = entry.TargetType,
                TargetId = entry.TargetId,
                BranchId = entry.BranchId,
                BranchName = branchName,
                Details = entry.Details != null ? JsonSerializer.Serialize(entry.Details) : null,
                IpAddress = entry.IpAddress,
                DeviceInfo = entry.DeviceInfo != null ? JsonSerializer.Serialize(entry.DeviceInfo) : null,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            _db.AuditLogs.Add(auditLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation("AUDIT: {Action} by {User} ({Role})", entry.Action, userName, entry.UserRole);
        }
        catch (Exception ex)
        {
            // SOP: Audit logging failures should never block operations but MUST be logged
            _logger.LogError(ex, "AUDIT LOG FAILURE — CRITICAL: {Action} by {User}", entry.Action, entry.UserName);
        }
    }
}
