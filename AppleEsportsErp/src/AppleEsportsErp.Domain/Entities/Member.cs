using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §14: Members Dashboard with wallet and loyalty points</summary>
public class Member
{
    public Guid Id { get; set; }
    public string MemberNumber { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public string MobileNumber { get; set; } = null!;
    public string? Email { get; set; }
    public string? Username { get; set; }       // nullable — set when operator assigns login
    public string? PasswordHash { get; set; }   // BCrypt hash
    
    // Password Reset fields
    public string? ResetToken { get; set; }
    public DateTimeOffset? ResetTokenExpiry { get; set; }

    // Brute-force lockout
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }

    public MemberStatus Status { get; set; } = MemberStatus.Active;

    // SOP §14.1: Wallet System - Separated per SOP §11.1
    public decimal GamingBalance { get; set; }
    public decimal FoodBalance { get; set; }

    /// <summary>
    /// When this balance was last known to be correct - the moment of the top-up or spend that
    /// produced it, not when the row happened to be saved.
    ///
    /// A member is shared across every branch on purpose: they join at one shop and should be
    /// able to spend at any of them. That only works if whichever branch or Head Office is
    /// holding the freshest figure can prove it is the freshest, rather than the two simply
    /// overwriting each other - two branches touching the same wallet minutes apart must not be
    /// able to undo one another depending on which sync happened to land last.
    /// </summary>
    public DateTimeOffset? BalanceAsOf { get; set; }

    // Lifetime running counters (never decrease on spend) — for the "how much of my balance
    // is real money vs bonus" breakdown shown in the Members UI. GamingBalance itself is the
    // one spendable pool and always shrinks on spend, same as before.
    public decimal TotalGamingTopUps { get; set; }
    public decimal TotalGamingBonusEarned { get; set; }

    // SOP §15: Loyalty Point System — gaming/food separated
    public int GamingPoints { get; set; }
    public int FoodPoints { get; set; }
    public int TotalPoints { get; set; }

    // Spending tracking — separated per SOP
    public decimal TotalGamingSpend { get; set; }
    public decimal TotalFoodSpend { get; set; }

    public Guid? HomeBranchId { get; set; }
    public DateTimeOffset JoinDate { get; set; }
    public DateTimeOffset? LastVisit { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation
    public Branch? HomeBranch { get; set; }
    public ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    public ICollection<LoyaltyPoint> LoyaltyPoints { get; set; } = new List<LoyaltyPoint>();
}
