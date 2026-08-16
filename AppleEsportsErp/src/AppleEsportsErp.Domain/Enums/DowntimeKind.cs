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
    InternetOffline,

    /// <summary>
    /// The app itself was broken or stuck, reported by an operator explaining a shift gap - not
    /// the branch's power, not their internet. Kept separate from <see cref="PowerOrRestart"/>
    /// deliberately: that kind's own report line claims sessions had their time credited back,
    /// which is only true for a genuine power cut. Filing a software bug under it would put a
    /// false claim on the owner's report on top of the false blame this exists to correct.
    /// </summary>
    AppFault
}
