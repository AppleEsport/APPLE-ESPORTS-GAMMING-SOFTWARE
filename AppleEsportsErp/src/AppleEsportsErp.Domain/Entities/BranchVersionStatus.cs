namespace AppleEsportsErp.Domain.Entities;

public class BranchVersionStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; }
    public string CurrentVersion { get; set; }
    public string LatestApprovedVersion { get; set; }

    /// <summary>
    /// On by default. A branch that has to be told to accept fixes will not be told, and
    /// four branches drifting onto different versions is how "it works at Adajan but not at
    /// Katargam" starts. Nothing installs until a Super Admin approves the version anyway,
    /// so the safety gate is approval, not this switch — an operator can still turn it off
    /// for a branch that wants to pick its moment.
    /// </summary>
    public bool AutoUpdateEnabled { get; set; } = true;
    public DateTime LastCheckedForUpdates { get; set; }
    public DateTime LastUpdated { get; set; }
    public int GamingPcsUpToDateCount { get; set; }
    public int GamingPcsTotalCount { get; set; }
}
