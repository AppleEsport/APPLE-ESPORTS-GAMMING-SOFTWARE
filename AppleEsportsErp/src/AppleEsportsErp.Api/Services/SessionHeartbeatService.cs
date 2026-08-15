using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Stamps every live session as "still running" on a short interval.
///
/// This is the register the system signs while the lights are on. It is deliberately
/// dumb — it records only that the system was alive at a moment in time. The value
/// comes later: on start-up, <see cref="SessionDowntimeRecovery"/> reads the gap
/// between the last stamp and now, and treats it as an outage the customer must not
/// be billed for.
///
/// Without this, elapsed time is pure wall-clock subtraction, so a thirty-minute
/// power cut silently eats thirty minutes of a customer's paid session.
///
/// Branch-only, and this one matters more than most. At Head Office it was stamping every
/// branch's sessions with Head Office's OWN liveness - so a branch that lost power for half an
/// hour still had an unbroken run of "system was alive" stamps written for it from Surat. The
/// gap SessionDowntimeRecovery looks for was being filled in by the wrong machine, and the
/// customer was billed for the power cut after all. See BranchOnlyBackgroundService.
/// </summary>
public class SessionHeartbeatService : BranchOnlyBackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SessionHeartbeatService> _logger;

    public SessionHeartbeatService(
        IServiceProvider services,
        ILogger<SessionHeartbeatService> logger,
        IConfiguration configuration)
        : base(configuration, logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SessionHeartbeatService starting (every {Interval}s).",
            SessionTimeCalculator.HeartbeatIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StampActiveSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A missed beat is harmless — the next one closes the gap, and a gap
                // under the downtime threshold is ignored as jitter.
                _logger.LogError(ex, "Failed to stamp session heartbeats.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(SessionTimeCalculator.HeartbeatIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SessionHeartbeatService stopping.");
    }

    private async Task StampActiveSessionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        // Single UPDATE statement — no entities loaded. This runs every 20 seconds
        // for the life of the process, so it must stay cheap.
        await db.Sessions
            .Where(s => s.State == SessionState.Active)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.LastHeartbeatAt, now),
                stoppingToken);
    }
}
