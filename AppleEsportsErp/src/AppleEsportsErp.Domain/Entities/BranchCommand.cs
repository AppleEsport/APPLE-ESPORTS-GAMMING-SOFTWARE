using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>
/// A remote start or stop, sent down for a branch to carry out and report back on.
///
/// This is the mechanism SessionService.RefuseIfHeadOffice pointed at instead of letting Head
/// Office write a session straight into its own database: "a command sent down for the branch
/// to carry out and report back, so both sides still agree afterwards." Delivery rides the
/// existing three-second heartbeat reply rather than a new channel, and the branch executes it
/// through the same ISessionService a counter operator's own click would use - so a remotely
/// started session appears on the counter screen exactly as if someone there had started it,
/// and can be billed and collected like any other.
/// </summary>
public class BranchCommand
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public Guid? PcId { get; set; }
    public CommandType Type { get; set; }

    /// <summary>The action's own data - customer name and package for a start, session id and deferPayment for a stop.</summary>
    public string PayloadJson { get; set; } = "{}";

    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    /// <summary>Set only on Failed - why the branch could not carry it out.</summary>
    public string? ResultMessage { get; set; }

    /// <summary>The session the branch created or ended, once known. Lets Head Office's UI find the row.</summary>
    public Guid? ResultSessionId { get; set; }

    public Guid? IssuedByOperatorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}
