using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Credits back time lost while the system was down — a power cut, a restart, an update.
///
/// Runs once at start-up, and deliberately <b>before</b> the session monitors get going:
/// <see cref="FixedDurationSessionMonitorService"/> auto-stops any session whose EndTime
/// has passed, so if it ran first it would close sessions that were only "expired" because
/// the branch was dark. Recovery has to move the goalposts before anyone checks them.
///
/// For each session still marked Active it compares now against the last heartbeat.
/// A gap beyond the jitter threshold is downtime: it is added to PausedSeconds (so
/// elapsed-time and billing exclude it) and EndTime is pushed forward by the same
/// amount (so a fixed-duration customer keeps the minutes they paid for).
///
/// The session is then put on hold rather than restarted. After an outage the customer
/// may have gone home, and quietly restarting the clock on an empty seat would bill them
/// for time nobody played. An operator resumes it when the customer is back at the PC,
/// or stops it and charges only for the minutes actually used.
/// </summary>
public static class SessionDowntimeRecovery
{
    public static async Task RunAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;

        var liveSessions = await db.Sessions
            .Where(s => s.State == SessionState.Active)
            .ToListAsync();

        if (liveSessions.Count == 0)
        {
            logger.LogInformation("Downtime recovery: no active sessions to check.");
            return;
        }

        var credited = 0;
        var flagged = 0;

        foreach (var session in liveSessions)
        {
            var downtimeSeconds = SessionTimeCalculator.DowntimeSecondsToCredit(session.LastHeartbeatAt, now);
            if (downtimeSeconds == 0)
            {
                // Either a clean restart, or a session that predates the heartbeat.
                // Either way there is nothing to give back.
                session.LastHeartbeatAt = now;
                continue;
            }

            if (SessionTimeCalculator.RequiresReview(downtimeSeconds))
            {
                // Too long to be a power cut. Almost certainly a session left open —
                // crediting it silently would hide the mistake, and charging for it
                // would be indefensible. Ask a human.
                session.NeedsTimeReview = true;
                session.State = SessionState.Interrupted;
                session.InterruptedAt = now;
                session.LastHeartbeatAt = now;
                flagged++;

                logger.LogWarning(
                    "Downtime recovery: session {SessionId} on branch {BranchId} has a {Gap:N0} minute gap " +
                    "since its last heartbeat — flagged for operator review rather than credited.",
                    session.Id, session.BranchId, downtimeSeconds / 60.0);
                continue;
            }

            session.PausedSeconds += downtimeSeconds;

            // A fixed-duration session stores an absolute finish time. Left alone, the
            // customer would lose exactly the minutes the shop was down, so move it.
            if (session.EndTime is { } endTime)
                session.EndTime = endTime.AddSeconds(downtimeSeconds);

            // Hold it, do not restart it — nobody knows yet whether the customer is
            // still in the building. The clock stays stopped until an operator says so.
            session.State = SessionState.Interrupted;
            session.InterruptedAt = now;
            session.LastHeartbeatAt = now;
            session.UpdatedAt = now;
            credited++;

            logger.LogInformation(
                "Downtime recovery: session {SessionId} held after a {Minutes:N1} minute outage " +
                "(total paused now {Total:N1} minutes) — awaiting operator resume or stop.",
                downtimeSeconds / 60.0, session.Id, session.PausedSeconds / 60.0);
        }

        await db.SaveChangesAsync();

        logger.LogInformation(
            "Downtime recovery complete: {Checked} active session(s) checked, {Held} held after an outage, {Flagged} flagged for review.",
            liveSessions.Count, credited, flagged);
    }
}
