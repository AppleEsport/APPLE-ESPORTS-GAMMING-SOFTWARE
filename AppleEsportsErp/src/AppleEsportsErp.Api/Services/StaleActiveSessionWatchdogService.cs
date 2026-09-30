using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Closes out a session whose PC has been silently gone for hours, instead of it sitting on
/// the Active Sessions list forever with an ever-climbing clock.
///
/// Found live at Citylight: a PC's agent dropped without ever going through the normal
/// lock/end sequence, and nothing else in the system ties "Active Sessions" to whether the PC
/// is actually still there - SessionHeartbeatService stamps every session marked Active as
/// "still alive" every 20 seconds regardless of the PC, and PcOverlayHub's OnDisconnectedAsync
/// does nothing with the disconnect. The session just stayed Active, climbing, for days.
///
/// Pc.LastAgentHeartbeat already exists and is already reliably maintained (DualConnectionService
/// heartbeats every 10 seconds and fails over LAN-to-Cloud within 30 - see
/// PcAgentWatchdogService's own remarks) - this reuses that same signal and the same 4-hour
/// staleness threshold that service already trusts, rather than inventing a new one.
///
/// PcAgentWatchdogService deliberately excludes a PC mid-session (State == Active) or
/// mid-bill (AwaitingBilling), on the assumption "those are billed and closed by the
/// machinery that already exists for that." This is that machinery's missing half: the
/// existing StopSessionAsync path only ever runs from a human's click, a planned-duration
/// timer, or a wallet running dry - never from a PC that simply stopped reporting in.
///
/// Bills up to the PC's LAST REAL CONTACT, not to whenever this happens to run - the same
/// "never bill for time nobody can vouch for" principle SessionDowntimeRecovery already
/// applies to power cuts. Stops with deferPayment: true, exactly as an operator would for a
/// customer who is no longer there to pay - the amount owed is recorded as a CustomerCredit
/// for the branch to collect or write off, and the PC is freed immediately rather than left
/// stuck on AwaitingBilling for a customer who has been gone for hours.
///
/// Branch-only, like every other job that acts on live PC/session state - see
/// BranchOnlyBackgroundService.
/// </summary>
public class StaleActiveSessionWatchdogService : BranchOnlyBackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Same threshold PcAgentWatchdogService already trusts for "the agent is
    /// genuinely gone, not just a flaky reconnect" - see that class's remarks for why 4 hours.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(4);

    private readonly IServiceProvider _services;
    private readonly ILogger<StaleActiveSessionWatchdogService> _logger;

    public StaleActiveSessionWatchdogService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<StaleActiveSessionWatchdogService> logger)
        : base(configuration, logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StaleActiveSessionWatchdogService is starting.");

        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseStaleSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed while checking for sessions whose PC has gone quiet.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("StaleActiveSessionWatchdogService is stopping.");
    }

    private async Task CloseStaleSessionsAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var cutoff = DateTimeOffset.UtcNow - StaleAfter;

        var stalePcs = await db.Pcs
            .Where(p => !p.IsDeleted
                && p.State == PcState.Active
                && p.CurrentSessionId != null
                && p.LastAgentHeartbeat != null
                && p.LastAgentHeartbeat < cutoff)
            .Select(p => new { p.Id, p.BranchId, p.PcNumber, p.CurrentSessionId, p.LastAgentHeartbeat })
            .ToListAsync(ct);

        if (stalePcs.Count == 0) return;

        foreach (var pc in stalePcs)
        {
            var session = await db.Sessions
                .Where(s => s.Id == pc.CurrentSessionId && s.State == SessionState.Active)
                .Select(s => new { s.Id, s.OperatorId })
                .FirstOrDefaultAsync(ct);

            if (session == null) continue;

            var quietFor = DateTimeOffset.UtcNow - pc.LastAgentHeartbeat!.Value;

            try
            {
                await sessionService.StopSessionAsync(
                    pc.BranchId, session.OperatorId, session.Id,
                    deferPayment: true, asOfOverride: pc.LastAgentHeartbeat);

                _logger.LogWarning(
                    "{PcNumber}: closed session {SessionId} automatically - its PC had not " +
                    "heartbeated in {Hours:0.#} hours. Billed up to the last real contact and " +
                    "recorded as a pending credit for the branch to collect.",
                    pc.PcNumber, session.Id, quietFor.TotalHours);

                await audit.LogAsync(new AuditEntry
                {
                    UserRole = "System",
                    UserName = "System",
                    Action = "session_auto_closed_stale_pc",
                    BranchId = pc.BranchId,
                    TargetType = "session",
                    TargetId = session.Id,
                    Details = new { pc.PcNumber, quietForHours = Math.Round(quietFor.TotalHours, 1) },
                });
            }
            catch (Exception ex)
            {
                // A missed pass is harmless - the next one tries again. Never let one bad
                // session stop the rest of the sweep.
                _logger.LogError(ex, "Failed to auto-close stale session {SessionId} on {PcNumber}", session.Id, pc.PcNumber);
            }
        }
    }
}
