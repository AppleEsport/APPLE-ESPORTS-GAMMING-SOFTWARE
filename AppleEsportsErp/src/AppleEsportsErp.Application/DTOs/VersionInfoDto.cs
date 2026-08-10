namespace AppleEsportsErp.Application.DTOs;

public class VersionInfoDto
{
    public int Id { get; set; }
    public string CurrentVersion { get; set; }
    public string ReleaseNotes { get; set; }
    public bool ApprovedForRollout { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string ApprovedByUserId { get; set; }
    public int BranchesApprovedCount { get; set; }
}

public class BranchVersionStatusDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; }
    public string CurrentVersion { get; set; }
    public string LatestApprovedVersion { get; set; }
    public bool AutoUpdateEnabled { get; set; }
    public bool UpdateAvailable { get; set; }
    public DateTime LastCheckedForUpdates { get; set; }
    public DateTime LastUpdated { get; set; }
    public int GamingPcsUpToDateCount { get; set; }
    public int GamingPcsTotalCount { get; set; }
}

public class ApproveUpdateDto
{
    public int VersionInfoId { get; set; }
}

public class UpdateBranchAutoUpdateDto
{
    public Guid BranchId { get; set; }
    public bool AutoUpdateEnabled { get; set; }
}
