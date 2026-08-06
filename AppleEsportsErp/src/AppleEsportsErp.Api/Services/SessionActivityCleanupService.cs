using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Infrastructure.Services;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Background service that runs weekly cleanup of old session activities.
/// Deletes activities older than 30 days every 7 days (Sunday at 02:00 AM UTC).
/// </summary>
public class SessionActivityCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SessionActivityCleanupService> _logger;
    private Timer? _timer;

    public SessionActivityCleanupService(IServiceProvider serviceProvider, ILogger<SessionActivityCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Session Activity Cleanup Service starting...");

        // Calculate time until next Sunday 02:00 AM UTC
        var now = DateTime.UtcNow;
        var nextCleanup = GetNextCleanupTime(now);
        var delayUntilNextRun = nextCleanup - now;

        _logger.LogInformation("Next cleanup scheduled for {NextCleanup} (in {Delay})", nextCleanup, delayUntilNextRun);

        // Wait until first scheduled time
        if (delayUntilNextRun > TimeSpan.Zero)
        {
            await Task.Delay(delayUntilNextRun, stoppingToken);
        }

        // Run cleanup weekly (every 7 days)
        _timer = new Timer(async _ => await RunCleanup(), null, TimeSpan.Zero, TimeSpan.FromDays(7));

        await Task.CompletedTask;
    }

    private async Task RunCleanup()
    {
        try
        {
            _logger.LogInformation("Starting session activity cleanup...");
            using var scope = _serviceProvider.CreateScope();
            var activityService = scope.ServiceProvider.GetRequiredService<ISessionActivityService>();
            await activityService.CleanupOldActivitiesAsync();
            _logger.LogInformation("Session activity cleanup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session activity cleanup failed");
        }
    }

    private static DateTime GetNextCleanupTime(DateTime now)
    {
        // Schedule for Sunday 02:00 AM UTC
        var nextSunday = now.AddDays(((int)DayOfWeek.Sunday - (int)now.DayOfWeek + 7) % 7);
        if (nextSunday == now.Date && now.Hour < 2)
        {
            return now.Date.AddHours(2);
        }
        return nextSunday.Date.AddHours(2);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Session Activity Cleanup Service stopping...");
        _timer?.Dispose();
        await base.StopAsync(cancellationToken);
    }
}
