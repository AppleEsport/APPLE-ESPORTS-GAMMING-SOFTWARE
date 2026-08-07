namespace AppleEsportsErp.Domain.Entities;

public class MaintenanceLog
{
    public Guid Id { get; set; }
    public Guid PcId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OperatorId { get; set; } // Id of whoever marked it — may be an Operator or a User (Admin/SuperAdmin)
    public string ActorRole { get; set; } = null!;
    public string MarkedByName { get; set; } = null!;
    public string? ResolvedByName { get; set; }

    public string Reason { get; set; } = null!; // Why marked for maintenance
    public DateTimeOffset MarkedAt { get; set; } // When marked for maintenance
    public DateTimeOffset? ResolvedAt { get; set; } // When resolved (if resolved)
    public string? ResolutionNotes { get; set; } // How it was fixed
    public bool IsResolved { get; set; }

    // Navigation — no navigation to Operator/User: OperatorId may reference either table
    // depending on ActorRole, so it's intentionally not FK-constrained.
    public Pc Pc { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
}
