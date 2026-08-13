namespace AppleEsportsErp.Application.DTOs.Sync;

/// <summary>
/// What a branch tells Head Office about itself, every thirty seconds.
///
/// Deliberately small. This is sent constantly and over a line that is sometimes a phone
/// tether, so it carries the shape of the shop rather than its contents: how many PCs are
/// busy, not which customer is on each.
/// </summary>
public class BranchHeartbeatDto
{
    public Guid BranchId { get; set; }
    public string? Version { get; set; }

    /// <summary>
    /// Which PC this came from, as Windows knows it.
    ///
    /// Added because two machines were both reporting as Adajan for an entire evening and
    /// nothing could say so. They overwrote each other every thirty seconds - one with an
    /// operator on shift, one with nobody - so the owner's screen flickered between two
    /// truths and neither was wrong. Finding it meant reading raw web server logs for
    /// source IP addresses.
    ///
    /// A name costs nothing to send and turns that into a sentence Head Office can print.
    /// It matters far beyond a confusing screen: each machine keeps its own database and
    /// syncs its own bills upward under the same branch, so with real money two shops'
    /// takings would merge into one and nothing would separate them again.
    /// </summary>
    public string? MachineName { get; set; }

    /// <summary>
    /// The configuration fingerprint this branch is currently running on.
    ///
    /// Head Office compares it with its own and sends the settings back only when they differ,
    /// which is what makes carrying configuration on a three-second beat affordable. Null from
    /// a branch that has never been told anything, and that is treated as "different".
    /// </summary>
    public string? ConfigVersion { get; set; }

    /// <summary>The branch's own clock. A shop with the wrong time produces reports nobody can reconcile.</summary>
    public DateTimeOffset BranchLocalTime { get; set; }

    public List<OperatorOnDutyDto> OperatorsOnDuty { get; set; } = new();

    /// <summary>Every PC and what it is doing, so Head Office's grid matches the counter's.</summary>
    public List<PcStateDto> Pcs { get; set; } = new();

    public int ActiveSessions { get; set; }

    /// <summary>Null when no drawer is open. Not zero — those mean different things.</summary>
    public decimal? DrawerExpected { get; set; }

    public decimal TakingsToday { get; set; }

    /// <summary>How far behind sync is. Zero means Head Office is seeing everything.</summary>
    public int UndeliveredRecords { get; set; }

    /// <summary>
    /// What became of commands this branch was handed on a previous beat.
    ///
    /// Carried on the next beat outward rather than as its own call, for the same reason
    /// config rides the reply inward: the connection already exists and already runs every
    /// three seconds. A result that never arrives here costs nothing but Head Office staying
    /// on "starting..." a little longer - the PC's own state, reported a few lines above in
    /// <see cref="Pcs"/>, catches up regardless and is the thing that can never lie.
    /// </summary>
    public List<BranchCommandResultDto> CommandResults { get; set; } = new();
}

/// <summary>What the branch did with a command Head Office sent down.</summary>
public class BranchCommandResultDto
{
    public Guid CommandId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public Guid? SessionId { get; set; }
}

/// <summary>A command as the branch receives it - Head Office's own id, kept as the branch's own id too.</summary>
public class BranchCommandDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid? PcId { get; set; }
    public string PayloadJson { get; set; } = "{}";
}

public class OperatorOnDutyDto
{
    public Guid OperatorId { get; set; }
    public string? FullName { get; set; }
    public DateTimeOffset ShiftStartedAt { get; set; }
}

public class PcStateDto
{
    public Guid PcId { get; set; }

    /// <summary>free, active, reserved, awaiting_billing, under_maintenance — the branch's own word for it.</summary>
    public string State { get; set; } = "idle";

    public Guid? CurrentSessionId { get; set; }
}
