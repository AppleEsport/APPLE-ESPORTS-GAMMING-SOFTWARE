namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §22: Immutable Audit Trail — every critical action logged, READ ONLY after creation</summary>
public class AuditLog
{
    public Guid Id { get; set; }
    // Who
    public Guid? UserId { get; set; }
    public Guid? OperatorId { get; set; }
    public string UserRole { get; set; } = null!;
    public string UserName { get; set; } = null!;
    // What
    public string Action { get; set; } = null!;
    public string? TargetType { get; set; }
    public Guid? TargetId { get; set; }

    /// <summary>
    /// Whether the thing being recorded actually worked. True by default, because most rows
    /// are a plain record of something that happened - a session started, a payment went
    /// through. Set false for an attempt that did not: a login that was rejected, a remote
    /// command the branch could not carry out, a session that refused to start.
    ///
    /// This did not exist before, and most failures were consequently invisible here entirely
    /// - the caller saw a red toast for a second and then it was gone, with nothing left to
    /// look back on. The two that already had their own action codes (failed_login,
    /// account_locked) are the exception, not the rule, and even those never set this - it
    /// defaulted to true, so a failed login's own row claimed to have succeeded.
    /// </summary>
    public bool Success { get; set; } = true;
    // Where
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    // Details
    public string? Details { get; set; } // JSONB
    public string? IpAddress { get; set; }
    public string? DeviceInfo { get; set; } // JSONB
    // When — SOP: exact date + timestamp
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Branch? Branch { get; set; }
}
