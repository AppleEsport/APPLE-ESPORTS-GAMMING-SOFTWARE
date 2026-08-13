namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// The living picture of a branch, as it was a moment ago.
///
/// Sync had one mechanism where it needed two. Events carry <b>history</b> - a bill was paid at
/// 19:04 for Rs 180 - and must never be lost, which is why they queue and retry for ever. But
/// "Ankur is on duty", "PC-03 is busy", "the drawer holds Rs 5,000" are not history. They are
/// <b>state</b>, and only the newest is worth anything.
///
/// Treating state as history is why the two never worked together. Every new thing anyone wanted
/// to see at Head Office needed its own event type, wired by hand in its own service - and the
/// list of what synced became "whatever somebody remembered". Sessions were remembered. Bills
/// were remembered. Operator status was not, so a branch trading all evening showed its staff as
/// logged out. PC state was not, so Head Office displayed July for four days.
///
/// One row per branch, overwritten in place. A heartbeat that goes missing costs nothing - the
/// next is thirty seconds away - so there is no queue, no retry, and nothing here can lose money.
/// That is exactly what makes it safe to send often.
///
/// It also answers the question the rebuild plan asks and nothing could: <i>is my branch
/// reporting?</i> A shop that has gone quiet is visible as quiet, rather than looking merely idle.
/// </summary>
public class BranchHeartbeat
{
    /// <summary>The branch this describes. One row each, replaced rather than appended.</summary>
    public Guid BranchId { get; set; }

    /// <summary>
    /// When Head Office last heard anything at all. The single most useful field here: a branch
    /// whose last beat was two hours ago has a problem, whatever the rest of this row says.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>The branch's own clock, so a wrong one at a shop is visible rather than puzzling.</summary>
    public DateTimeOffset BranchLocalTime { get; set; }

    public string? Version { get; set; }

    /// <summary>The counter PC this branch is being run from, as Windows names it.</summary>
    public string? ReportedByMachine { get; set; }

    /// <summary>
    /// A second machine claiming to be this same branch, if one has been heard from.
    ///
    /// Two counter PCs both reporting as one shop is not a curiosity, it is a slow disaster:
    /// each keeps its own database and sends its own bills upward under the same branch, so
    /// the takings of two shops merge into one set of figures that can never be separated
    /// again. It happened here for a whole evening and the only trace was a web server log.
    ///
    /// Recorded rather than rejected. Head Office genuinely cannot tell which of the two is
    /// the real Adajan - refusing the newcomer would as easily silence the true counter PC
    /// after a hardware replacement. Naming both and letting a person decide is the only
    /// honest answer.
    /// </summary>
    public string? ConflictingMachine { get; set; }

    /// <summary>When the clash was last seen. Null once nothing has disagreed for a while.</summary>
    public DateTimeOffset? ConflictingMachineSeenAt { get; set; }

    /// <summary>Who is on shift right now, as JSON: name, when they started, their shift.</summary>
    public string? OperatorsOnDuty { get; set; }

    public int OperatorsOnDutyCount { get; set; }
    public int ActiveSessions { get; set; }
    public int PcsBusy { get; set; }
    public int PcsTotal { get; set; }

    /// <summary>
    /// What the drawer is expected to hold at this moment. Null when no drawer is open, which
    /// is different from zero and must not be shown as it.
    /// </summary>
    public decimal? DrawerExpected { get; set; }

    /// <summary>Takings so far in the current trading day, as the branch itself counts them.</summary>
    public decimal TakingsToday { get; set; }

    /// <summary>
    /// Records the branch has queued and not yet delivered. Money Head Office cannot see yet,
    /// and the number that says whether sync is keeping up.
    /// </summary>
    public int UndeliveredRecords { get; set; }
}
