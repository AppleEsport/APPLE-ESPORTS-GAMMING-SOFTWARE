using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §10: Shift accountability — operator shift records</summary>
public class Shift
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset LoginTime { get; set; }
    public DateTimeOffset? LogoutTime { get; set; }
    public string? DeviceInfo { get; set; } // JSONB
    public ShiftStatus Status { get; set; } = ShiftStatus.Active;
    /// <summary>SOP §18: Shift Summary stored at closure — JSONB</summary>
    /// <summary>
    /// Set when the operator ticks "this is the last shift of the day" on the way out.
    ///
    /// One tick answers two questions that cannot be answered reliably any other way. It
    /// closes the trading day, which is when the day's summary is sent. And it says the
    /// quiet that follows is the shop being shut, not a fault - without it, turning the
    /// cafe off at night looks exactly like a power cut, and every branch would report one
    /// every single night.
    /// </summary>
    public bool ClosedTradingDay { get; set; }

    /// <summary>
    /// The operator who closed this shift, when it was not its own.
    ///
    /// Null on every normal close - the operator ended their own shift and there is nothing to
    /// say. Set when somebody else closed it: the next person in, who found it abandoned,
    /// counted the drawer and shut it down.
    ///
    /// Kept apart from <see cref="OperatorId"/> deliberately. A shift closed by somebody else
    /// still records takings against the operator who took them, and a shortfall found at that
    /// count still belongs to the shift it was found in - but the count itself was made by a
    /// different person, and reading one as the other puts missing money against the name of
    /// somebody who was not there.
    /// </summary>
    public Guid? ClosedByOperatorId { get; set; }

    public string? Summary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Operator Operator { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<CashRegister> CashRegisters { get; set; } = new List<CashRegister>();
}
