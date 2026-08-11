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

    // ── How an update is going, as reported by the branch itself ──
    //
    // Here so the progress bar on the Updates page has something true to show. The alternative
    // - animating a bar on a timer once somebody presses Update Now - looks identical while it
    // works and lies outright when it does not: a stuck download and a finished one would show
    // the same thing. Nothing writes these until the branch app exists, and until then the page
    // says so rather than pretending.

    /// <summary>
    /// "downloading", "installing", "restarting", "done" or "failed". Null means nothing is
    /// happening, which is the normal state.
    /// </summary>
    public string? UpdateStage { get; set; }

    /// <summary>0-100, meaningful during "downloading". Best effort — some stages cannot report.</summary>
    public int UpdateProgressPercent { get; set; }

    /// <summary>
    /// What to show the operator, in plain English — including why it failed, if it did. A
    /// failure with no reason attached just moves the confusion from the branch to the owner.
    /// </summary>
    public string? UpdateMessage { get; set; }

    /// <summary>
    /// When the current stage began. A bar that has not moved in twenty minutes is a stuck
    /// update, and without a timestamp there is no way to tell that from a slow one.
    /// </summary>
    public DateTime? UpdateStageChangedAt { get; set; }
}
