namespace AppleEsportsErp.Domain.Enums;

/// <summary>
/// What kind of outage a <see cref="Entities.DowntimeEvent"/> records. The two are found by
/// completely different means and have opposite consequences for the customer, so they are
/// never conflated on a report.
/// </summary>
public enum DowntimeKind
{
    /// <summary>
    /// The branch system itself stopped — a power cut, a restart, an update. Detected as a
    /// hole in the session heartbeat. Play actually stopped, so customers get their time back.
    /// </summary>
    PowerOrRestart,

    /// <summary>
    /// The link to Head Office was lost while the branch carried on running. Detected by the
    /// sync courier failing to deliver. Nobody's game was interrupted and no time is credited —
    /// it is recorded so the owner knows why figures arrived late.
    /// </summary>
    InternetOffline
}
