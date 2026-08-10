using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Infrastructure.Services;

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
        var notifier = scope.ServiceProvider.GetRequiredService<IAdminNotifier>();

        var now = DateTimeOffset.UtcNow;

        // Branch comes along for the ride: deciding whether an outage needs a human to
        // look at it depends on that branch's trading hours, not on how long it lasted.
        var liveSessions = await db.Sessions
            .Include(s => s.Branch)
            .Where(s => s.State == SessionState.Active)
            .ToListAsync();

        if (liveSessions.Count == 0)
        {
            logger.LogInformation("Downtime recovery: no active sessions to check.");
            return;
        }

        var credited = 0;
        var flagged = 0;

        // One outage hits a whole branch at once, so the report wants one row per branch,
        // not one per session. Keyed by branch, widened to cover every session it touched.
        var outages = new Dictionary<Guid, DowntimeEvent>();

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

            var gapStart = session.LastHeartbeatAt!.Value;
            var needsReview = session.Branch is { } branch
                && SessionTimeCalculator.RequiresReview(gapStart, now, branch.OpeningTime, branch.ClosingTime);

            RecordOutage(outages, session.BranchId, gapStart, now);

            if (needsReview)
            {
                // The gap runs through hours this branch is shut, so it is far more likely
                // a session nobody stopped than a power cut with a customer waiting.
                // Crediting it silently would hide the mistake, and charging for it would
                // be indefensible. Ask a human.
                session.NeedsTimeReview = true;
                session.State = SessionState.Interrupted;
                session.InterruptedAt = now;
                session.LastHeartbeatAt = now;
                flagged++;

                logger.LogWarning(
                    "Downtime recovery: session {SessionId} on branch {BranchId} has a {Gap:N0} minute gap " +
                    "({From} to {To} IST) running through closed hours — flagged for operator review " +
                    "rather than credited.",
                    session.Id, session.BranchId, downtimeSeconds / 60.0,
                    IndiaTime.Format(gapStart), IndiaTime.Format(now));
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

        // A branch whose last shift was marked "last of the day" was shut on purpose. The
        // silence that follows is the cafe being closed, not a power cut, and recording one
        // every night would bury the real ones and email the owner daily for nothing. Only
        // reached when an operator left a session running before shutting down; a clean close
        // has no live sessions and never gets this far.
        var expected = new List<Guid>();
        foreach (var (branchId, outage) in outages)
        {
            var closedDeliberately = await db.Shifts
                .Where(s => s.BranchId == branchId
                            && s.ClosedTradingDay
                            && s.LogoutTime != null
                            && s.LogoutTime <= outage.StartedAt.AddMinutes(30))
                .OrderByDescending(s => s.LogoutTime)
                .AnyAsync();

            if (closedDeliberately) expected.Add(branchId);
        }

        foreach (var branchId in expected)
        {
            logger.LogInformation(
                "Branch {BranchId} was shut deliberately at the end of the day - not recording a power cut.",
                branchId);
            outages.Remove(branchId);
        }

        if (outages.Count > 0)
            db.DowntimeEvents.AddRange(outages.Values);

        await db.SaveChangesAsync();

        foreach (var outage in outages.Values)
        {
            logger.LogInformation(
                "Downtime recorded for branch {BranchId}: {From} to {To} IST ({Minutes:N0} min), " +
                "{Sessions} session(s) affected — will appear on the {Day} report.",
                outage.BranchId, IndiaTime.Format(outage.StartedAt), IndiaTime.Format(outage.EndedAt),
                outage.DurationSeconds / 60.0, outage.SessionsAffected, outage.BusinessDay);
        }

        foreach (var outage in outages.Values)
        {
            var branchName = await db.Branches.Where(b => b.Id == outage.BranchId)
                .Select(b => b.Name).FirstOrDefaultAsync() ?? "Unknown branch";

            var howLong = AdminEmailTemplate.Describe(TimeSpan.FromSeconds(outage.DurationSeconds));

            await notifier.NotifyAsync(
                $"{branchName} went off for {howLong} - {outage.SessionsAffected} customer(s) playing",
                AdminEmailTemplate.Compose(
                    $"{branchName} lost power",
                    AdminEmailTemplate.Red,
                    $"The system at {branchName} stopped running for {howLong}. This is usually a power cut, but a restart or a Windows update looks the same. " +
                    (outage.SessionsAffected > 0
                        ? $"{outage.SessionsAffected} customer{(outage.SessionsAffected == 1 ? " was" : "s were")} playing at the time."
                        : "Nobody was playing at the time."),
                    new[]
                    {
                        ("Branch", branchName),
                        ("", ""),
                        ("Went off at", IndiaTime.Format(outage.StartedAt)),
                        ("Came back at", IndiaTime.Format(outage.EndedAt)),
                        ("Off for", howLong),
                        ("", ""),
                        ("Customers playing", outage.SessionsAffected.ToString()),
                        ("Counts towards", $"{outage.BusinessDay:dd MMM yyyy}"),
                    },
                    headline: $"Off for {howLong}",
                    footnote: "Nobody has been charged for the time the system was off. Those customers' timers are paused, and the operator is asked whether each person is still there before any more time is counted."));
        }

        logger.LogInformation(
            "Downtime recovery complete: {Checked} active session(s) checked, {Held} held after an outage, {Flagged} flagged for review.",
            liveSessions.Count, credited, flagged);
    }

    /// <summary>
    /// Folds a session's gap into a single per-branch outage row. Sessions on the same branch
    /// share one power cut, so the report shows "Adajan was down 20:30-22:00, 6 PCs affected"
    /// rather than six near-identical rows. The window widens to the earliest start seen,
    /// since heartbeats do not all land on the same tick.
    /// </summary>
    private static void RecordOutage(
        Dictionary<Guid, DowntimeEvent> outages, Guid branchId, DateTimeOffset gapStart, DateTimeOffset now)
    {
        if (outages.TryGetValue(branchId, out var existing))
        {
            if (gapStart < existing.StartedAt)
            {
                existing.StartedAt = gapStart;
                existing.DurationSeconds = (int)(existing.EndedAt - gapStart).TotalSeconds;
                existing.BusinessDay = IndiaTime.BusinessDayOf(gapStart);
            }
            existing.SessionsAffected++;
            return;
        }

        outages[branchId] = new DowntimeEvent
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Kind = DowntimeKind.PowerOrRestart,
            StartedAt = gapStart,
            EndedAt = now,
            DurationSeconds = (int)(now - gapStart).TotalSeconds,
            SessionsAffected = 1,
            // Reported against the night it disrupted, not the morning it was noticed.
            BusinessDay = IndiaTime.BusinessDayOf(gapStart),
            Notes = "System was not running — power cut, restart or update.",
            CreatedAt = now,
        };
    }
}
