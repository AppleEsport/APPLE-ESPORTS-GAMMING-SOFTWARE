namespace AppleEsportsErp.Domain.Entities;

public class BranchVersionStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; }
    public string CurrentVersion { get; set; }
    public string LatestApprovedVersion { get; set; }
    public bool AutoUpdateEnabled { get; set; }
    public DateTime LastCheckedForUpdates { get; set; }
    public DateTime LastUpdated { get; set; }
    public int GamingPcsUpToDateCount { get; set; }
    public int GamingPcsTotalCount { get; set; }
}
