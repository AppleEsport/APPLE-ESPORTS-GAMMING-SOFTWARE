using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Stops a pay-as-you-go member the moment their wallet is used up — and tells them first.
///
/// This used to ask "has he gone over?" once a minute, which by definition only notices once he
/// has: ₹10 buys ten minutes, the check fired at minute eleven, and the member was left owing ₹1
/// that was never there. The stopping point is arithmetic, known the moment the session starts,
/// so it is now worked out in advance and the session stopped there.
///
/// It also said nothing to the member. From the seat, a session that ends by itself is
/// indistinguishable from a machine that has died.
/// </summary>
/// <remarks>
/// Branch-only. This one draws money out of a member's wallet, and at Head Office it would do
/// so against every branch's synced sessions at once - the same member debited twice for one
/// hour of play. See BranchOnlyBackgroundService.
/// </remarks>
public class OpenSessionMonitorService : BranchOnlyBackgroundService
{
    /// <summary>
    /// Three times a minute. Frequent enough that the stop lands close to the calculated moment,
    /// and the safety margin below covers the rest — being a little late must not cost the member
    /// anything, so the two are set together.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(20);

    /// <summary>Long enough to walk to the counter and top up.</summary>
    private const int WarnWhenMinutesLeft = 5;

    private readonly IServiceProvider _services;
    private readonly ILogger<OpenSessionMonitorService> _logger;

    /// <summary>
    /// Sessions already warned, so the member gets one message rather than one every twenty
    /// seconds for five minutes. Held in memory on purpose: the cost of forgetting after a
    /// restart is one repeated warning, which is not worth a database column.
    /// </summary>
    private readonly HashSet<Guid> _warned = new();

    public OpenSessionMonitorService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<OpenSessionMonitorService> logger)
        : base(configuration, logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OpenSessionMonitorService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOpenSessionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing CheckOpenSessionsAsync.");
            }

            await Task.Delay(TickInterval, stoppingToken);
        }

        _logger.LogInformation("OpenSessionMonitorService is stopping.");
    }

    private async Task CheckOpenSessionsAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubNotificationService>();

        var now = DateTimeOffset.UtcNow;

        // Find active open sessions for members
        var openSessions = await db.Sessions
            .Include(s => s.Pc)
                .ThenInclude(p => p.PricingProfile)
            .Where(s => s.State == SessionState.Active && s.PlannedDurationMin == null && s.MemberId != null)
            .ToListAsync(stoppingToken);

        if (!openSessions.Any())
            return;

        foreach (var session in openSessions)
        {
            var member = await db.Members.FindAsync(new object[] { session.MemberId! }, stoppingToken);
            if (member == null) continue;

            // Excludes any downtime already credited back, so a power cut never burns
            // through a member's wallet balance while nobody was playing.
            var actualDurationMin = SessionTimeCalculator.ElapsedMinutes(
                session.StartTime, session.PausedSeconds, now);

            // Rate comes from the PC's own Pricing Profile, same as StopSessionAsync uses to
            // bill — each branch/PC can charge differently, so a hardcoded rate here would
            // trigger this safety net too early or too late depending on the branch.
            decimal ratePerHour = session.Pc?.PricingProfile?.BaseHourlyRate ?? SessionPricingCalculator.DefaultRatePerHour;
            int bufferMinutes = session.Pc?.PricingProfile?.BufferMinutes ?? SessionPricingCalculator.DefaultBufferMinutes;

            // Headroom for this loop being a little late — at least a rupee, and at a dearer PC
            // a whole minute of play, since a minute costs more there. Deliberately derived from
            // the tick interval: if that is ever lengthened, the margin follows it rather than
            // silently becoming too small and putting members into debt.
            decimal safetyRupees = Math.Max(1m, ratePerHour / 60m * (decimal)(TickInterval.TotalSeconds * 3 / 60));

            // When their money runs out, worked out up front rather than discovered afterwards.
            decimal stopAtMinutes = SessionPricingCalculator.AffordableMinutes(
                ratePerHour, bufferMinutes, member.GamingBalance, safetyRupees);

            // A PC with no rate configured bills nothing, so there is no limit to enforce.
            if (stopAtMinutes == decimal.MaxValue) continue;

            var minutesLeft = stopAtMinutes - actualDurationMin;

            if (minutesLeft <= 0m)
            {
                _logger.LogInformation(
                    "Stopping session {SessionId}: wallet spent. Balance was {Wallet}, played {Played:0.#}m of an affordable {Affordable:0.#}m.",
                    session.Id, member.GamingBalance, actualDurationMin, stopAtMinutes);

                try
                {
                    // Told before the PC locks. Afterwards the message would arrive on a screen
                    // the member can no longer see, which is the whole complaint being fixed.
                    if (session.PcId != Guid.Empty)
                        await hub.SendWalletFinishedToAgentAsync(session.PcId);

                    // StopSessionAsync deducts the wallet and settles the bill itself.
                    await sessionService.StopSessionAsync(session.BranchId, session.OperatorId, session.Id);

                    _warned.Remove(session.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to stop session {SessionId} whose wallet ran out", session.Id);
                }

                continue;
            }

            // Warned once, while they can still do something about it.
            if (minutesLeft <= WarnWhenMinutesLeft && _warned.Add(session.Id))
            {
                try
                {
                    if (session.PcId != Guid.Empty)
                        await hub.SendWalletRunningOutToAgentAsync(
                            session.PcId, (int)Math.Floor(minutesLeft), member.GamingBalance);
                }
                catch (Exception ex)
                {
                    // A warning that cannot be delivered must not stop the session being stopped
                    // on time further down the line.
                    _logger.LogError(ex, "Could not warn the member on session {SessionId}", session.Id);
                    _warned.Remove(session.Id);
                }
            }
        }

        // Sessions that have since ended must not accumulate here for the life of the process.
        _warned.RemoveWhere(id => openSessions.All(s => s.Id != id));
    }
}
