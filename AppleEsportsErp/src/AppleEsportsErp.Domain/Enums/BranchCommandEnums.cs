namespace AppleEsportsErp.Domain.Enums;

/// <summary>
/// What Head Office is asking a branch to do.
///
/// Deliberately small. Only actions that were previously refused outright at the branch
/// (starting/stopping play from Head Office - see SessionService.RefuseIfHeadOffice) go
/// through here, and only because this path makes them safe: the branch carries the action
/// out through its own normal code, on its own database, so it is never invisible to the
/// counter the way a session written straight into Head Office's database would be.
/// </summary>
public enum CommandType
{
    StartSession,
    StopSession,
}

/// <summary>
/// Where a command stands. Moves forward only - nothing here is ever re-opened once it lands
/// in Confirmed or Failed, so a super admin's screen can trust the last thing it was told.
/// </summary>
public enum CommandStatus
{
    /// <summary>Written at Head Office, not yet seen by the branch.</summary>
    Pending,

    /// <summary>Handed to the branch on a heartbeat reply; not yet acknowledged.</summary>
    Delivered,

    /// <summary>The branch carried it out.</summary>
    Confirmed,

    /// <summary>The branch tried and it did not work - reason in ResultMessage.</summary>
    Failed,
}
