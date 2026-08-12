namespace AppleEsportsErp.Application.DTOs.Eod;

public class EodReportDto
{
    public Guid BranchId { get; set; }
    public DateTimeOffset ReportDate { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    
    public RevenueSummaryDto Revenue { get; set; } = new();
    public CashSummaryDto Cash { get; set; } = new();
    public PaymentMethodSummaryDto PaymentMethods { get; set; } = new();
    public ShiftSummaryDto Shifts { get; set; } = new();
    public OperationalStatsDto Operations { get; set; } = new();
    public List<EodCreditLogDto> CreditLogs { get; set; } = new();
}

public class EodCreditLogDto
{
    public Guid CreditId { get; set; }
    public string CustomerName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string PcNumber { get; set; } = null!;
    public decimal OriginalBillAmount { get; set; }
    public decimal AmountPaidInitially { get; set; }
    public decimal CreditAmount { get; set; }
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
}

public class RevenueSummaryDto
{
    public decimal TotalGamingRevenue { get; set; }
    public decimal TotalFoodRevenue { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal NetRevenue { get; set; }
}

public class CashSummaryDto
{
    public decimal TotalOpeningBalance { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalCashInwards { get; set; }
    public decimal TotalPettyExpenses { get; set; }
    public decimal TotalOwnerWithdrawals { get; set; }
    public decimal ExpectedCashInDrawer { get; set; }

    /// <summary>
    /// What the drawer currently on the counter was counted at. **Null when nobody has counted
    /// it yet**, which is the normal state until a shift ends.
    ///
    /// Nullable rather than zero on purpose. Zero reads as "the drawer is empty" — the whole
    /// day's takings shown as gone — when what is true is "nobody has looked".
    /// </summary>
    public decimal? ActualPhysicalCashCounted { get; set; }

    /// <summary>
    /// <see cref="ActualPhysicalCashCounted"/> minus <see cref="ExpectedCashInDrawer"/>, so the
    /// three figures always agree with each other. Null while the drawer is uncounted, because
    /// the difference is then unknown, and "no difference" on an uncounted drawer is the most
    /// dangerous thing this screen could say.
    /// </summary>
    public decimal? TotalDiscrepancy { get; set; }

    /// <summary>
    /// Differences already found and closed earlier in the same trading day — a shift handover
    /// where the drawer was counted short, or over.
    ///
    /// Kept as its own figure rather than folded into <see cref="TotalDiscrepancy"/>. That money
    /// went missing on an earlier shift, and the drawer in use now started from what was actually
    /// counted, so it is not itself short by this amount. Without the line the column does not
    /// add up: opening plus takings comes to more than the drawer is expected to hold, with
    /// nothing on screen explaining the gap.
    /// </summary>
    public decimal DifferencesFoundEarlier { get; set; }
}

public class PaymentMethodSummaryDto
{
    public decimal TotalCash { get; set; }
    public decimal TotalOnline { get; set; }
    public decimal TotalWalletDeductions { get; set; }
    public decimal TotalWalletTopUps { get; set; }
}

public class ShiftSummaryDto
{
    public int TotalShifts { get; set; }
    public List<ShiftDetailDto> ShiftDetails { get; set; } = new();
}

public class ShiftDetailDto
{
    public Guid ShiftId { get; set; }
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = null!;
    public decimal TotalSales { get; set; }
    public decimal CashDiscrepancy { get; set; }
}

public class OperationalStatsDto
{
    public int TotalSessions { get; set; }
    public int TotalReservations { get; set; }
    public int TotalFoodOrders { get; set; }
    public int NewMembersRegistered { get; set; }
}
