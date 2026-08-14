namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// Something Head Office has asked a branch to do, and the branch's own record of doing it.
///
/// This is the missing half of the two-way link. Sync moved history upward (a session
/// happened) and, since the config work, settings downward (an operator is now allowed X).
/// Neither carries an instruction - "stop this session" - and there was no safe way to add
/// one, because writing directly into the branch's tables from Head Office is exactly what
/// created an unbillable session on ADJ-PC-01: a fact invented on one side that the other side
/// never agreed to.
///
/// A command fixes that by never writing the fact itself. Head Office states an intent and
/// waits; the branch is the one that actually stops the session, through the same code path an
/// operator's own click would use, and reports back what happened. Both sides end up agreeing
/// because only one side ever acted.
/// </summary>
public class BranchCommand
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }

    /// <summary>
    /// What to do, as a short word the branch looks up rather than code it has to already know
    /// about. New command types can be added without every existing branch build recognising
    /// them - an unknown type is simply left pending until an update teaches the branch what it
    /// means, rather than crashing on something it cannot parse.
    /// </summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>The specifics - which session, for instance - as JSON, since every command type needs different ones.</summary>
    public string Payload { get; set; } = "{}";

    public BranchCommandStatus Status { get; set; } = BranchCommandStatus.Pending;

    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this rode down in a heartbeat reply. Null until the branch has actually seen it.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the branch reported back, successful or not.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>What the branch says happened - the amount billed, or why it could not be done.</summary>
    public string? ResultMessage { get; set; }
}

/// <summary>
/// Deliberately few states, each meaning one thing on one side.
///
/// Pending and Sent are Head Office's bookkeeping - has the branch even heard about this yet.
/// Succeeded and Failed are the branch's own account of what happened, and only the branch is
/// ever allowed to write them: Head Office asked, it does not get to decide the answer.
/// </summary>
public enum BranchCommandStatus
{
    Pending,
    Sent,
    Succeeded,
    Failed,
}
