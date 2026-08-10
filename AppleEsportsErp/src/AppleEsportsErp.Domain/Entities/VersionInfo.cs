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

    // ── The installer this version refers to ──

    /// <summary>
    /// File name of the installer held by Head Office, e.g. "AppleEsports-Setup-2.1.0.exe".
    /// A name rather than a full URL, so branches always fetch from the Head Office they are
    /// already talking to and a stale row can never point them somewhere else.
    /// </summary>
    public string? InstallerFileName { get; set; }

    /// <summary>
    /// SHA-256 of that installer, checked by the branch before it runs anything.
    ///
    /// This is the part that matters. A branch downloads an executable and runs it with full
    /// privileges; without verifying it, anyone able to interfere with the connection could
    /// hand all four branches a program of their choosing. Branches talk to Head Office over
    /// plain HTTP today, so this hash is currently the only thing standing in the way.
    /// An update with no hash recorded is refused rather than trusted.
    /// </summary>
    public string? InstallerSha256 { get; set; }

    public long InstallerSizeBytes { get; set; }
}
