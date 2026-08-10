using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Domain.Entities;

/// <summary>SOP §10: Physical Cash Drawer Management</summary>
public class CashRegister
{
    public Guid Id { get; set; }

    /// <summary>The shift that opened the drawer. Kept for accountability, not for scoping.</summary>
    public Guid ShiftId { get; set; }

    /// <summary>
    /// The trading day (06:00-06:00 IST) this drawer belongs to.
    ///
    /// One drawer per day, not one per login. There is a single physical cash box on the
    /// counter; tying it to a shift meant an operator who logged in three times opened three
    /// registers and was asked to count the same money three times, producing three sets of
    /// figures that reconciled against nothing. Whoever is on duty adds to the same drawer,
    /// and it is counted once at the end of the day.
    /// </summary>
    public DateOnly BusinessDay { get; set; }

    public Guid BranchId { get; set; }
    public Guid OperatorId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalSplitCash { get; set; }
    public decimal ExpectedDrawerCash { get; set; }
    public decimal? PhysicalCashCounted { get; set; }
    public decimal? CashDifference { get; set; }
    public string? MismatchReason { get; set; }
    public CashRegisterStatus Status { get; set; } = CashRegisterStatus.Open;
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    // Navigation
    public Shift Shift { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public Operator Operator { get; set; } = null!;
    public ICollection<CashTransaction> CashTransactions { get; set; } = new List<CashTransaction>();
    public ICollection<DenominationCount> DenominationCounts { get; set; } = new List<DenominationCount>();
}
