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
