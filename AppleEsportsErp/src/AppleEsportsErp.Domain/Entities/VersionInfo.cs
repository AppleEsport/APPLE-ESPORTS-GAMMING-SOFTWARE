namespace AppleEsportsErp.Domain.Entities;

public class VersionInfo
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
