using AppleEsportsErp.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace AppleEsportsErp.Application.DTOs.Cash;

public class CashRegisterDto
{
    public Guid Id { get; set; }
    public Guid ShiftId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OperatorId { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalSplitCash { get; set; }
    public decimal ExpectedDrawerCash { get; set; }
    public decimal? PhysicalCashCounted { get; set; }
    public decimal? CashDifference { get; set; }
    public string? MismatchReason { get; set; }
    public CashRegisterStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    
    public List<CashTransactionDto> Transactions { get; set; } = new();
}

public class CashTransactionDto
{
    public Guid Id { get; set; }
    public Guid? BillId { get; set; }
    public string? PcNumber { get; set; }
    public decimal CashAmount { get; set; }
    public decimal CashReceived { get; set; }
    public decimal ChangeReturned { get; set; }
    public decimal ActualCashCollected { get; set; }
    public decimal GamingAmount { get; set; }
    public decimal FoodAmount { get; set; }
    public string TransactionType { get; set; } = null!;
    public string? CustomerName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What the shift-start screen needs to know before it asks anything about cash.</summary>
public class RegisterOpeningDto
{
    /// <summary>
    /// True when nothing has opened a drawer yet today, so this operator is putting the float
    /// in and is the only person who will be asked for a figure.
    /// </summary>
    public bool IsFirstOfDay { get; set; }

    /// <summary>
    /// What the drawer will open with when it is not the first of the day: what the last shift
    /// counted, or failing that what the system expected them to have. Nobody is asked to
    /// confirm or retype it — it is money that has already been counted once.
    /// </summary>
    public decimal InheritedBalance { get; set; }

    /// <summary>True when a drawer is already open and this operator simply carries on with it.</summary>
    public bool AlreadyOpen { get; set; }
}

public class OpenRegisterDto
{
    [Required]
    [Range(0, 1000000)]
    public decimal OpeningBalance { get; set; }
}

public class AddCashTransactionDto
{
    [Required]
    [Range(-1000000, 1000000)]
    public decimal Amount { get; set; }
    
    [Required]
    public string TransactionType { get; set; } = null!; // petty_expense, withdrawal, inward
    [Required(ErrorMessage = "Reason is mandatory for manual cash transactions.")]
    public string Reason { get; set; } = null!;
}
