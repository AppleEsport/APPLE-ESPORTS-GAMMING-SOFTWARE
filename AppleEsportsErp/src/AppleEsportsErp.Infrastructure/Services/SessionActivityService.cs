using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

public interface ISessionActivityService
{
    Task LogActivityAsync(Guid sessionId, Guid branchId, string action, string description, decimal? amount = null);
    Task<IEnumerable<SessionActivity>> GetSessionActivitiesAsync(Guid sessionId);
    Task<IEnumerable<SessionActivity>> GetRecentActivitiesAsync(Guid branchId, int limit = 100);
    Task CleanupOldActivitiesAsync();
}

public class SessionActivityService : ISessionActivityService
{
    private readonly AppDbContext _db;
    private readonly ILogger<SessionActivityService> _logger;
    private const int RetentionDays = 30;

    public SessionActivityService(AppDbContext db, ILogger<SessionActivityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogActivityAsync(Guid sessionId, Guid branchId, string action, string description, decimal? amount = null)
    {
        try
        {
            var activity = new SessionActivity
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                BranchId = branchId,
                Action = action,
                Description = description,
                Amount = amount,
                Timestamp = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.SessionActivities.Add(activity);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Logged activity '{Action}' for session {SessionId}", action, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log activity for session {SessionId}", sessionId);
        }
    }

    public async Task<IEnumerable<SessionActivity>> GetSessionActivitiesAsync(Guid sessionId)
    {
        return await _db.SessionActivities
            .AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<SessionActivity>> GetRecentActivitiesAsync(Guid branchId, int limit = 100)
    {
        return await _db.SessionActivities
            .AsNoTracking()
            .Where(a => a.BranchId == branchId)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();
    }

    public async Task CleanupOldActivitiesAsync()
    {
        try
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            var deletedCount = await _db.SessionActivities
                .Where(a => a.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync();

            _logger.LogInformation("Cleanup completed: Deleted {Count} old session activities", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old session activities");
        }
    }
}
