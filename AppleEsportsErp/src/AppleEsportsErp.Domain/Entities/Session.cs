using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §7: Session Engine — full gaming session lifecycle</summary>
public class Session
{
    public Guid Id { get; set; }
    public Guid PcId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OperatorId { get; set; }
    public Guid? ShiftId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? MemberId { get; set; }

    // Session timing
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public int? PlannedDurationMin { get; set; }
    public int? ActualDurationMin { get; set; }

    // ── Downtime tolerance (power cuts, restarts, failover) ──
    // Elapsed time cannot be derived from the wall clock alone: if the branch loses
    // power for 30 minutes, the clock still advances but the customer was not playing.
    // A heartbeat stamps LastHeartbeatAt while the session is live; on start-up the gap
    // since the last heartbeat is treated as downtime and credited back to the customer.

    /// <summary>Last moment the system confirmed this session was actually running.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; set; }

    /// <summary>Total seconds of downtime credited back — never billed to the customer.</summary>
    public int PausedSeconds { get; set; }

    /// <summary>
    /// When the session was put on hold after an outage. The wait between the power
    /// returning and an operator deciding what to do is also not play time, so this is
    /// folded into <see cref="PausedSeconds"/> the moment the session is resumed or stopped.
    /// </summary>
    public DateTimeOffset? InterruptedAt { get; set; }

    /// <summary>
    /// Set when a recovered gap was too long to credit automatically (for example a PC left
    /// on overnight with a session open). The operator is asked to confirm rather than the
    /// system silently resuming or silently charging.
    /// </summary>
    public bool NeedsTimeReview { get; set; }

    // Billing — SOP: gaming and food MUST remain separated
    public decimal GamingAmount { get; set; }
    public decimal FoodAmount { get; set; }
    public decimal TotalAmount { get; set; }

    // State
    public SessionState State { get; set; } = SessionState.Active;
    public string GamingType { get; set; } = "standard";
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Pc Pc { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public Operator Operator { get; set; } = null!;
    public Shift? Shift { get; set; }
    public Member? Member { get; set; }
    public ICollection<Bill> Bills { get; set; } = new List<Bill>();
    public ICollection<FoodOrder> FoodOrders { get; set; } = new List<FoodOrder>();
    public ICollection<SessionActivity> Activities { get; set; } = new List<SessionActivity>();
}
