using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §7.1: PC stations per branch with state tracking</summary>
public class Pc
{
    public Guid Id { get; set; }
    public string PcNumber { get; set; } = null!;
    public Guid BranchId { get; set; }

    /// <summary>
    /// Starts as <see cref="PcState.AwaitingSetup"/>, not Idle. A PC record with no physical
    /// machine behind it must not look bookable — seating a customer at one would leave them
    /// staring at a locked screen. Setup flips it to Idle.
    /// </summary>
    public PcState State { get; set; } = PcState.AwaitingSetup;
    public Guid? CurrentSessionId { get; set; }
    public Guid? CurrentReservationId { get; set; }
    public DateTimeOffset? LastActiveAt { get; set; }
    public Guid? LastOperatorId { get; set; }
    public string? IpAddress { get; set; }
    public string? Specs { get; set; } // JSONB
    public string? PcName { get; set; }
    public string? Zone { get; set; }
    public Guid? PricingProfileId { get; set; }
    public string? HardwareNotes { get; set; }
    public string? MonitorHz { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    
    // ── Setup / provisioning ──
    // A PC record is claimed by exactly one physical machine, once. Re-running setup on the
    // same machine is fine (a reinstall or repair), but a second machine cannot take a PC
    // number that is already in use — two machines answering as "PC-1" would mean unlock
    // commands going to the wrong screen.

    /// <summary>
    /// Hardware fingerprint of the machine that claimed this PC. Null means not set up yet.
    /// </summary>
    public string? MachineId { get; set; }

    /// <summary>Secret issued at setup; the agent presents it on every later call.</summary>
    public string? MachineToken { get; set; }

    public DateTimeOffset? ProvisionedAt { get; set; }

    /// <summary>True once a physical machine has completed setup against this record.</summary>
    public bool IsProvisioned => MachineId != null;

    // Agent connection tracking
    public bool IsAgentOnline { get; set; } = false;
    public string ConnectionMode { get; set; } = "None";  // "LAN", "Cloud", "None"
    public DateTimeOffset? LastAgentHeartbeat { get; set; }
    
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Branch Branch { get; set; } = null!;
    public Session? CurrentSession { get; set; }
    public Reservation? CurrentReservation { get; set; }
    public Operator? LastOperator { get; set; }
    public PricingProfile? PricingProfile { get; set; }
    public ICollection<Session> Sessions { get; set; } = new List<Session>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<MaintenanceLog> MaintenanceLogs { get; set; } = new List<MaintenanceLog>();
}
