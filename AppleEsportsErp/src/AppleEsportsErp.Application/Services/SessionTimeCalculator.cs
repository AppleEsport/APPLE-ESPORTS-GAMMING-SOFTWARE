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
    /// Longest gap credited back without a human looking at it. Beyond this the
    /// session is flagged for the operator instead — a PC left on overnight with a
    /// session open is not the same thing as a power cut, and quietly crediting
    /// fourteen hours would hide a real mistake.
    /// </summary>
    public const int MaxAutoCreditMinutes = 240;

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
    /// True when a recovered gap is too large to credit silently and an operator
    /// should confirm what actually happened.
    /// </summary>
    public static bool RequiresReview(int downtimeSeconds) =>
        downtimeSeconds > MaxAutoCreditMinutes * 60;
}
