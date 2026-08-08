namespace AppleEsportsErp.Application.Services;

/// <summary>
/// Single source of truth for "how long has this customer actually been playing".
///
/// Elapsed time cannot be taken from the wall clock alone. If the branch loses power
/// for thirty minutes the clock still advances, but the customer was sitting in the
/// dark — billing them for it is simply wrong. A heartbeat records that a session is
/// genuinely running; on start-up the gap since the last heartbeat is treated as
/// downtime and credited back.
///
/// Used identically by final billing, the live PC-status feed, the open-session
/// monitor and the member overlay, so every screen agrees on the same number.
/// </summary>
public static class SessionTimeCalculator
{
    /// <summary>How often a live session is stamped as still running.</summary>
    public const int HeartbeatIntervalSeconds = 20;

    /// <summary>
    /// Gaps shorter than this are normal scheduling jitter, not an outage, and are
    /// ignored. Comfortably above the heartbeat interval so a slow tick never reads
    /// as downtime, and low enough to catch the 5-10 second LAN/cloud failover.
    /// </summary>
    public const int DowntimeThresholdSeconds = 60;

    /// <summary>
    /// Absolute backstop. A gap this long has to have crossed a closing time, whatever
    /// the branch's hours are, so it is never credited without a human looking at it.
    /// </summary>
    private static readonly TimeSpan FullDay = TimeSpan.FromHours(24);

    /// <summary>
    /// Minutes the customer has genuinely been playing: wall-clock time since start,
    /// less any downtime already credited. Never negative.
    /// </summary>
    public static decimal ElapsedMinutes(DateTimeOffset startTime, int pausedSeconds, DateTimeOffset now)
    {
        decimal wallClock = (decimal)(now - startTime).TotalMinutes;
        decimal paused = pausedSeconds / 60m;
        return Math.Max(0m, wallClock - paused);
    }

    /// <summary>
    /// Given the last confirmed heartbeat, how many seconds of downtime should be
    /// credited. Returns 0 when the gap is within normal jitter, or when there is no
    /// heartbeat yet (a session started before this feature existed, or one that has
    /// not had its first tick).
    /// </summary>
    public static int DowntimeSecondsToCredit(DateTimeOffset? lastHeartbeatAt, DateTimeOffset now)
    {
        if (lastHeartbeatAt is null) return 0;

        double gap = (now - lastHeartbeatAt.Value).TotalSeconds;
        return gap < DowntimeThresholdSeconds ? 0 : (int)gap;
    }

    /// <summary>
    /// True when an outage should be questioned rather than credited automatically.
    ///
    /// Two very different things leave an identical gap in the record, and length alone
    /// cannot separate them:
    ///
    ///   A real power cut happens <b>while the branch is open</b>, with the customer sitting
    ///   there waiting for the lights. However long it lasts, they deserve their time back.
    ///
    ///   A session nobody stopped runs on past closing, through the shut hours, until someone
    ///   opens up the next morning. Crediting that would hand back hours to a customer who
    ///   went home before midnight.
    ///
    /// So the test is <i>when</i>, not <i>how long</i>: a gap touching the hours the branch is
    /// closed gets flagged for the operator. A four-hour cut during trading is still credited.
    /// </summary>
    public static bool RequiresReview(
        DateTimeOffset gapStart, DateTimeOffset gapEnd, TimeOnly openingTime, TimeOnly closingTime)
    {
        if (gapEnd <= gapStart) return false;

        // A branch that never closes has no shut hours to catch anything.
        if (openingTime == closingTime) return false;

        // Long enough that it must have crossed a closing time.
        if (gapEnd - gapStart >= FullDay) return true;

        var startIst = IndiaTime.ToIst(gapStart);
        var endIst = IndiaTime.ToIst(gapEnd);
        var closedFor = ClosedDuration(openingTime, closingTime);

        // The gap is under 24h, so at most three calendar days can be involved.
        for (var day = startIst.Date.AddDays(-1); day <= endIst.Date.AddDays(1); day = day.AddDays(1))
        {
            var closedStart = new DateTimeOffset(day.Add(closingTime.ToTimeSpan()), IndiaTime.Offset);
            var closedEnd = closedStart.Add(closedFor);

            // Half-open overlap test between [closedStart, closedEnd) and [startIst, endIst).
            if (closedStart < endIst && startIst < closedEnd)
                return true;
        }

        return false;
    }

    /// <summary>
    /// How long the branch is shut each day. Handles trading that runs past midnight —
    /// open 10:00 and close 02:00 means eight hours closed, not minus eight.
    /// </summary>
    private static TimeSpan ClosedDuration(TimeOnly openingTime, TimeOnly closingTime)
    {
        var shut = openingTime.ToTimeSpan() - closingTime.ToTimeSpan();
        return shut > TimeSpan.Zero ? shut : shut + TimeSpan.FromDays(1);
    }
}
