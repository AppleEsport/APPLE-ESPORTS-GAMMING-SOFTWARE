namespace AppleEsportsErp.Application.DTOs.Sync;

/// <summary>
/// What Head Office tells a branch about itself, in the reply to a heartbeat.
///
/// Everything in this system flowed one way - upward. A branch reported its sessions, its
/// takings, its state, and Head Office listened. Nothing came back. So a super admin could
/// tick "End of Day" for an operator, watch the server save it, and the counter would never
/// hear of it. Every setting on the server was decoration.
///
/// It travels in the heartbeat reply rather than over anything new. A branch is already
/// speaking to Head Office every three seconds and already reading the answer, so a change
/// lands within three seconds of being made, over a connection that exists, with no second
/// mechanism to keep working.
///
/// Sent only when something actually changed. The branch reports the <see cref="Version"/> it
/// currently holds and Head Office replies with nothing at all when it matches - so the
/// ordinary case stays a few hundred bytes, and the full list only crosses the wire on the
/// rare beat after somebody edits a permission.
///
/// This is deliberately configuration and never operational state. Who exists and what they
/// are allowed to see belongs to Head Office. Who is on shift right now belongs to the branch,
/// which is the only place that can know it.
/// </summary>
public class BranchConfigDto
{
    /// <summary>
    /// Fingerprint of everything below. The branch sends back the one it holds; equal means
    /// nothing needs sending. Derived from the contents, so it changes when they do and no
    /// separate bookkeeping can fall out of step with it.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    public List<BranchOperatorConfigDto> Operators { get; set; } = new();

    /// <summary>
    /// The food menu as Head Office defines it for this branch.
    ///
    /// This is the fix for a super admin adding an item at Head Office and it never appearing
    /// at the counter: the Menu Editor is branch-scoped storage, so an item "added for Adajan"
    /// while working at Head Office was written into Head Office's own copy of Adajan's table
    /// and nowhere else - the physical Adajan counter runs an entirely separate database that
    /// was never told.
    /// </summary>
    public List<BranchMenuItemConfigDto> MenuItems { get; set; } = new();

    /// <summary>
    /// Every member and their current wallet balance, as Head Office holds it.
    ///
    /// Members are deliberately not branch-scoped - someone joins at Adajan and should be able
    /// to spend their wallet at Katargam - which means every branch legitimately needs to know
    /// about every member, not just its own. Sent whole rather than as a delta because a
    /// branch's local copy can be arbitrarily stale after time offline, and reconciling a
    /// balance from a partial history invites exactly the kind of drift this exists to remove.
    /// </summary>
    public List<BranchMemberConfigDto> Members { get; set; } = new();
}

/// <summary>
/// One operator as Head Office defines them.
///
/// Enough to create the person at a branch that has never heard of them, which closes the
/// other half of the same gap: an operator added at Head Office could not log in at the shop
/// they had just been hired for.
/// </summary>
public class BranchOperatorConfigDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The stored hash, never a password - the same value the branch would have written itself
    /// had the operator been created there. Without it a new operator exists at the shop and
    /// cannot sign in, which is worse than not existing.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string? MobileNumber { get; set; }
    public string? AccessPin { get; set; }
    public bool IsGlobalAdmin { get; set; }

    /// <summary>The permission map, verbatim. This is the thing that was never arriving.</summary>
    public string DashboardPermissions { get; set; } = string.Empty;

    /// <summary>
    /// Whether Head Office has barred this person - suspended or disabled.
    ///
    /// Sent as a plain yes or no rather than as a status, because the status column carries two
    /// unrelated ideas: an administrator's decision about a person, and whether they happen to
    /// be standing at a counter. The first travels down; the second is the branch's alone and
    /// must never be overwritten from here, or a heartbeat would sign out the operator who is
    /// mid-shift.
    /// </summary>
    public bool IsBlocked { get; set; }
}

/// <summary>
/// One food item's catalog details - what it is called, what it costs, whether it is for sale.
///
/// Deliberately not the whole InventoryItem. CurrentStock and SoldQty are the branch's own
/// trading state, exactly like a PC's busy/idle - they change constantly at the counter and
/// must never be overwritten by a heartbeat reply, or a shop that just sold its last plate of
/// fries would have Head Office silently restock it on the next beat.
/// </summary>
public class BranchMenuItemConfigDto
{
    public Guid Id { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal Price { get; set; }
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Withdrawn from sale by Head Office, as opposed to Out of Stock - which is the branch's
    /// own call and is deliberately not carried here, for the same reason CurrentStock is not.
    /// </summary>
    public bool IsDisabled { get; set; }
}

/// <summary>One member and the wallet balance Head Office currently holds for them.</summary>
public class BranchMemberConfigDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string MemberNumber { get; set; } = string.Empty;
    public string MobileNumber { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Username { get; set; }
    public decimal GamingBalance { get; set; }
    public decimal FoodBalance { get; set; }

    /// <summary>
    /// When this balance was last known correct. Sent so a branch can tell whether its own,
    /// more recent, local activity for this member is actually newer than what Head Office is
    /// handing back - and if so, keep its own figure rather than being overwritten by a beat
    /// that has not caught up yet.
    /// </summary>
    public DateTimeOffset? BalanceAsOf { get; set; }

    public bool IsBlocked { get; set; }
}
