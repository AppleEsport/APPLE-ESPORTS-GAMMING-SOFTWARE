namespace AppleEsportsErp.Application.DTOs.PcManagement;

public class MaintenanceLogDto
{
    public Guid Id { get; set; }
    public Guid PcId { get; set; }
    public string PcName { get; set; } = null!;
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public DateTimeOffset MarkedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool IsResolved { get; set; }
    public int DurationMinutes => IsResolved && ResolvedAt.HasValue
        ? (int)(ResolvedAt.Value - MarkedAt).TotalMinutes
        : (int)(DateTimeOffset.UtcNow - MarkedAt).TotalMinutes;
}

public class MarkMaintenanceDto
{
    public Guid PcId { get; set; }
    public Guid BranchId { get; set; }
    public string Reason { get; set; } = null!;
}

public class ResolveMaintenanceDto
{
    public string? ResolutionNotes { get; set; }
}
