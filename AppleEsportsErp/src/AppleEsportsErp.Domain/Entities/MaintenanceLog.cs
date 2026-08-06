namespace AppleEsportsErp.Domain.Entities;

public class MaintenanceLog
{
    public Guid Id { get; set; }
    public Guid PcId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OperatorId { get; set; }

    public string Reason { get; set; } = null!; // Why marked for maintenance
    public DateTimeOffset MarkedAt { get; set; } // When marked for maintenance
    public DateTimeOffset? ResolvedAt { get; set; } // When resolved (if resolved)
    public string? ResolutionNotes { get; set; } // How it was fixed
    public bool IsResolved { get; set; }

    // Navigation
    public Pc Pc { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public Operator Operator { get; set; } = null!;
}
