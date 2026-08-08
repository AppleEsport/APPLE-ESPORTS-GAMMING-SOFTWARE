namespace AppleEsportsErp.Domain.Enums;

/// <summary>SOP §7.1: PC States — Green=Active, Purple=Reserved, Orange=Awaiting, Gray=Idle, Red=Offline</summary>
public enum PcState
{
    Idle,
    Active,
    Reserved,
    AwaitingBilling,
    Offline,
    UnderMaintenance,
    CloudMode,       // Agent connected via internet, Operator PC unreachable

    /// <summary>
    /// The PC record exists but no physical machine has claimed it yet. It is deliberately
    /// NOT Idle: an unconfigured PC must never appear bookable, or an operator will seat a
    /// customer at a machine that cannot be unlocked. Becomes Idle the moment an agent
    /// completes setup. Stored as "awaitingsetup".
    /// </summary>
    AwaitingSetup
}
