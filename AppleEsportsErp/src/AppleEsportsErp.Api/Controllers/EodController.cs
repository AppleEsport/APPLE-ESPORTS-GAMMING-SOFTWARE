using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Eod;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.Services;
using System.Security.Claims;

using Microsoft.EntityFrameworkCore;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/eod")]
[Authorize]
[BranchIsolation]
public class EodController : ControllerBase
{
    private readonly IEodService _eodService;
    private readonly IUnitOfWork _unitOfWork;

    public EodController(IEodService eodService, IUnitOfWork unitOfWork)
    {
        _eodService = eodService;
        _unitOfWork = unitOfWork;
    }

    [HttpGet("range-report")]
    public async Task<IActionResult> GetRangeReport(
        [FromQuery] DateTimeOffset? startDate, 
        [FromQuery] DateTimeOffset? endDate, 
        [FromQuery] Guid? branchId = null)
    {
        var targetBranchId = branchId ?? GetBranchId();
        
        var startUtc = (startDate ?? DateTimeOffset.UtcNow.AddDays(-30)).ToUniversalTime();
        var endUtc = (endDate ?? DateTimeOffset.UtcNow).ToUniversalTime();

        var bills = await _unitOfWork.Repository<AppleEsportsErp.Domain.Entities.Bill>()
            .Query()
            .Include(b => b.DiscountByAdmin)
            .Include(b => b.Operator)
            .Include(b => b.Session)
            .Include(b => b.Pc)
            .Where(b => b.BranchId == targetBranchId
                     && b.Status == AppleEsportsErp.Domain.Enums.BillStatus.Completed 
                     && b.CompletedAt >= startUtc 
                     && b.CompletedAt <= endUtc)
            .ToListAsync();

        // Bucketed by the 06:00-06:00 IST trading day, the same day boundary
        // /eod/preview and /eod/finalize use - not the raw UTC calendar date, which
        // puts a bill rung up at 01:30 IST on the wrong day here and the right day
        // everywhere else.
        var billsByBusinessDay = bills
            .Select(b => (Bill: b, BusinessDay: IndiaTime.BusinessDayOf(b.CompletedAt!.Value)))
            .ToList();

        var dailyReport = billsByBusinessDay
            .GroupBy(x => x.BusinessDay)
            .Select(g => new {
                Date = g.Key.ToString("yyyy-MM-dd"),
                GamingRevenue = g.Sum(x => x.Bill.Subtotal > 0 ? x.Bill.GamingAmount - (x.Bill.GamingAmount / x.Bill.Subtotal * x.Bill.DiscountAmount) : x.Bill.GamingAmount),
                FoodRevenue = g.Sum(x => x.Bill.Subtotal > 0 ? x.Bill.FoodAmount - (x.Bill.FoodAmount / x.Bill.Subtotal * x.Bill.DiscountAmount) : x.Bill.FoodAmount),
                DiscountAmount = g.Sum(x => x.Bill.DiscountAmount),
                TotalRevenue = g.Sum(x => x.Bill.TotalAmount)
            })
            .OrderBy(r => r.Date)
            .ToList();

        var monthlyReport = billsByBusinessDay
            .GroupBy(x => new { x.BusinessDay.Year, x.BusinessDay.Month })
            .Select(g => new {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                GamingRevenue = g.Sum(x => x.Bill.Subtotal > 0 ? x.Bill.GamingAmount - (x.Bill.GamingAmount / x.Bill.Subtotal * x.Bill.DiscountAmount) : x.Bill.GamingAmount),
                FoodRevenue = g.Sum(x => x.Bill.Subtotal > 0 ? x.Bill.FoodAmount - (x.Bill.FoodAmount / x.Bill.Subtotal * x.Bill.DiscountAmount) : x.Bill.FoodAmount),
                DiscountAmount = g.Sum(x => x.Bill.DiscountAmount),
                TotalRevenue = g.Sum(x => x.Bill.TotalAmount)
            })
            .OrderBy(r => r.Month)
            .ToList();

        var discountAudit = bills
            .Where(b => b.DiscountAmount > 0)
            .Select(b => new {
                BillId = b.BillNumber,
                Date = b.CompletedAt,
                Subtotal = b.Subtotal,
                DiscountAmount = b.DiscountAmount,
                DiscountType = b.DiscountType?.ToString(),
                DiscountValue = b.DiscountValue,
                DiscountReason = b.DiscountReason,
                GivenBy = b.DiscountByAdmin != null 
                    ? $"Super Admin ({b.DiscountByAdmin.FullName})" 
                    : (b.Operator != null ? $"Operator ({b.Operator.FullName})" : "Unknown")
            })
            .OrderByDescending(d => d.Date)
            .ToList();

        var billIds = bills.Select(b => b.Id).ToList();
        var billCredits = await _unitOfWork.Repository<AppleEsportsErp.Domain.Entities.CustomerCredit>()
            .Query()
            .Where(c => billIds.Contains(c.BillId))
            .ToListAsync();

        var allBills = bills.Select(b => {
            var credit = billCredits.FirstOrDefault(c => c.BillId == b.Id);
            var actualPaid = b.CashAmount + b.OnlineAmount + b.WalletAmount;
            var isCredit = credit != null || actualPaid < b.TotalAmount || b.PaymentType?.ToString() == "Credit";
            
            return new {
                BillId = b.BillNumber,
                Date = b.CompletedAt,
                Operator = b.Operator != null ? b.Operator.FullName : "Unknown",
                Customer = string.IsNullOrEmpty(b.CustomerName) ? "Walk-in" : b.CustomerName,
                GamingRevenue = b.GamingAmount,
                FoodRevenue = b.FoodAmount,
                Discount = b.DiscountAmount,
                TotalRevenue = b.TotalAmount,
                PaymentType = isCredit ? "Credit" : (b.PaymentType?.ToString() ?? "Unknown"),
                AmountPaidInitially = credit != null ? credit.AmountPaidInitially : actualPaid,
                CreditAmount = credit != null ? credit.CreditAmount : (isCredit ? Math.Max(0, b.TotalAmount - actualPaid) : 0),
                CreditStatus = isCredit ? "pending" : null,
                SessionNotes = b.Session?.Notes,
                SessionStartTime = b.Session != null ? b.Session.StartTime : (DateTimeOffset?)null,
                SessionEndTime = b.Session != null ? b.Session.EndTime : (DateTimeOffset?)null,
                SessionDurationMinutes = b.Session != null && b.Session.EndTime.HasValue 
                    ? (b.Session.EndTime.Value - b.Session.StartTime).TotalMinutes 
                    : 0,
                PcId = b.PcId,
                PcName = b.Pc != null ? b.Pc.PcNumber : "Walk-in"
            };
        })
        .OrderByDescending(b => b.Date)
        .ToList();

        var credits = await _unitOfWork.Repository<AppleEsportsErp.Domain.Entities.CustomerCredit>()
            .Query()
            .Include(c => c.Bill)
            .Include(c => c.ClearedByOperator)
            .Where(c => c.BranchId == targetBranchId 
                     && ((c.CreatedAt >= startUtc && c.CreatedAt <= endUtc) || (c.ClearedAt >= startUtc && c.ClearedAt <= endUtc)))
            .ToListAsync();

        var clearedPastCredits = credits
            .Where(c => c.Status != null && c.Status.ToLower() == "cleared" && c.ClearedAt >= startUtc && c.ClearedAt <= endUtc)
            .Select(c => new {
                BillId = $"SETTLED-{(c.Bill != null ? c.Bill.BillNumber : "CREDIT")}",
                Date = c.ClearedAt,
                Operator = c.ClearedByOperator != null ? c.ClearedByOperator.FullName : "Unknown",
                Customer = string.IsNullOrEmpty(c.CustomerName) ? "Walk-in" : c.CustomerName,
                GamingRevenue = 0m,
                FoodRevenue = 0m,
                Discount = 0m,
                TotalRevenue = c.CreditAmount,
                PaymentType = "CREDIT SETTLED",
                AmountPaidInitially = c.AmountPaidInitially,
                CreditAmount = c.CreditAmount,
                CreditStatus = "cleared",
                SessionNotes = "Credit clearance payment for past session",
                SessionStartTime = c.Bill != null ? c.Bill.CreatedAt : c.CreatedAt,
                SessionEndTime = (DateTimeOffset?)null,
                SessionDurationMinutes = 0d,
                PcId = (Guid?)null,
                PcName = c.PcNumber ?? "N/A"
            })
            .Cast<object>()
            .ToList();

        var walletTopUps = await _unitOfWork.Repository<AppleEsportsErp.Domain.Entities.WalletTransaction>()
            .Query()
            .Include(t => t.Member)
            .Include(t => t.Operator)
            .Where(t => t.BranchId == targetBranchId
                     && t.Action == AppleEsportsErp.Domain.Enums.WalletAction.Recharge
                     && t.CreatedAt >= startUtc
                     && t.CreatedAt <= endUtc)
            .ToListAsync();

        var walletTopUpRows = walletTopUps
            .Select(t => new {
                BillId = $"TOPUP-{t.Id.ToString().Substring(0, 8).ToUpper()}",
                Date = t.CreatedAt,
                Operator = t.Operator != null ? t.Operator.FullName : "Unknown",
                Customer = t.Member != null ? t.Member.FullName : "Unknown",
                GamingRevenue = 0m,
                FoodRevenue = 0m,
                Discount = 0m,
                TotalRevenue = t.Amount,
                PaymentType = $"Wallet Top-Up ({t.PaymentType ?? "cash"})",
                AmountPaidInitially = t.Amount,
                CreditAmount = 0m,
                CreditStatus = (string?)null,
                SessionNotes = t.Reason ?? $"{t.TargetWallet} wallet top-up",
                // A top-up is an instant, not a session, so it has a time but no duration.
                // Both were left null, which rendered as "-" and made it impossible to tell
                // from the day's audit log when money had actually gone into a wallet —
                // the one thing you need when a customer disputes a top-up.
                SessionStartTime = (DateTimeOffset?)t.CreatedAt,
                SessionEndTime = (DateTimeOffset?)null,
                SessionDurationMinutes = 0d,
                PcId = (Guid?)null,
                PcName = "-"
            })
            .Cast<object>()
            .ToList();

        var allBillsList = allBills.Cast<object>().ToList();
        var combinedBills = allBillsList.Concat(clearedPastCredits).Concat(walletTopUpRows)
            .OrderByDescending(b => ((dynamic)b).Date)
            .ToList();

        var allCredits = credits.Select(c => new {
            CreditId = c.Id,
            CustomerName = c.CustomerName,
            CustomerPhone = c.CustomerPhone,
            PcNumber = c.PcNumber,
            OriginalBillAmount = c.OriginalBillAmount,
            AmountPaidInitially = c.AmountPaidInitially,
            CreditAmount = c.CreditAmount,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            ClearedAt = c.ClearedAt
        })
        .OrderByDescending(c => c.CreatedAt)
        .ToList();

        // Power cuts and lost connections for this trading day.
        //
        // Reported alongside the money because they explain it. An evening that looks thin
        // is a very different conversation once you can see the branch was dark for forty
        // minutes, and an operator should not have to remember and argue the point.
        var downtime = await _unitOfWork.Repository<AppleEsportsErp.Domain.Entities.DowntimeEvent>()
            .Query()
            .Where(d => d.BranchId == targetBranchId && d.StartedAt >= startUtc && d.StartedAt <= endUtc)
            .OrderBy(d => d.StartedAt)
            .ToListAsync();

        var downtimeRows = downtime.Select(d => new
        {
            d.Id,
            Kind = d.Kind == AppleEsportsErp.Domain.Enums.DowntimeKind.PowerOrRestart
                ? "Power cut / restart"
                : "Internet offline",
            // Formatted in IST here rather than left to the browser, because this is also
            // what goes onto the printed report.
            From = IndiaTime.FormatTime(d.StartedAt),
            To = IndiaTime.FormatTime(d.EndedAt),
            Minutes = Math.Round(d.DurationSeconds / 60.0, 0),
            d.SessionsAffected,
            // A power cut stops play and customers get their time back; losing the link to
            // Head Office does not interrupt anybody's game. Saying so on the report stops
            // the two being read as the same event.
            Impact = d.Kind == AppleEsportsErp.Domain.Enums.DowntimeKind.PowerOrRestart
                ? "Play stopped — affected sessions had their time credited back"
                : "Play unaffected — only the link to Head Office was down",
            d.Notes,
        }).ToList();

        return Ok(ApiResponse<object>.Ok(new {
            Daily = dailyReport,
            Monthly = monthlyReport,
            Discounts = discountAudit,
            AllBills = combinedBills,
            AllCredits = allCredits,
            Downtime = downtimeRows,
            DowntimeTotalMinutes = Math.Round(downtime.Sum(d => d.DurationSeconds) / 60.0, 0),
        }));
    }

    private Guid GetBranchId() => Guid.Parse(HttpContext.Items["BranchId"]!.ToString()!);

    [HttpGet("report")]
    [HttpGet("preview")]
    public async Task<IActionResult> GetPreview([FromQuery] string? date)
    {
        DateTimeOffset targetDate;

        if (string.IsNullOrWhiteSpace(date))
        {
            targetDate = DateTimeOffset.UtcNow.ToUniversalTime();
        }
        else if (DateTimeOffset.TryParse(date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            targetDate = parsed.ToUniversalTime();
        }
        else if (DateTime.TryParse(date, out var dateOnly))
        {
            // Handle "YYYY-MM-DD" format from React component
            targetDate = new DateTimeOffset(dateOnly.Date, TimeSpan.Zero);
        }
        else
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid date format. Use ISO 8601 format or YYYY-MM-DD."));
        }

        var result = await _eodService.GenerateEodReportAsync(GetBranchId(), targetDate);
        return Ok(ApiResponse<EodReportDto>.Ok(result));
    }

    [HttpGet("validation")]
    public async Task<IActionResult> GetValidationStatus([FromQuery] string? date)
    {
        DateTimeOffset targetDate;

        if (string.IsNullOrWhiteSpace(date))
        {
            targetDate = DateTimeOffset.UtcNow.ToUniversalTime();
        }
        else if (DateTimeOffset.TryParse(date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            targetDate = parsed.ToUniversalTime();
        }
        else if (DateTime.TryParse(date, out var dateOnly))
        {
            // Handle "YYYY-MM-DD" format from React component
            targetDate = new DateTimeOffset(dateOnly.Date, TimeSpan.Zero);
        }
        else
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid date format. Use ISO 8601 format or YYYY-MM-DD."));
        }

        var result = await _eodService.GetValidationStatusAsync(GetBranchId(), targetDate);
        return Ok(ApiResponse<ValidationStatusDto>.Ok(result));
    }

    [HttpPost("finalize")]
    [Authorize(Roles = Roles.SuperAdmin)] // Strictly SuperAdmin as per SOP
    public async Task<IActionResult> FinalizeEod([FromBody] FinalizeEodRequest request)
    {
        DateTimeOffset targetDate;

        if (string.IsNullOrWhiteSpace(request.Date))
        {
            targetDate = DateTimeOffset.UtcNow.ToUniversalTime();
        }
        else if (DateTimeOffset.TryParse(request.Date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            targetDate = parsed.ToUniversalTime();
        }
        else if (DateTime.TryParse(request.Date, out var dateOnly))
        {
            // Handle "YYYY-MM-DD" format from React component
            targetDate = new DateTimeOffset(dateOnly.Date, TimeSpan.Zero);
        }
        else
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid date format. Use ISO 8601 format or YYYY-MM-DD."));
        }

        var result = await _eodService.FinalizeEodAsync(GetBranchId(), (await this.GetOperatorIdAsync()), targetDate);
        return Ok(ApiResponse<EodSnapshotDto>.Ok(result));
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistoricalEod([FromQuery] string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return BadRequest(ApiResponse<object>.Fail("Date parameter is required."));
        }

        DateTimeOffset targetDate;
        if (DateTimeOffset.TryParse(date, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            targetDate = parsed.ToUniversalTime();
        }
        else if (DateTime.TryParse(date, out var dateOnly))
        {
            // Handle "YYYY-MM-DD" format from React component
            targetDate = new DateTimeOffset(dateOnly.Date, TimeSpan.Zero);
        }
        else
        {
            return BadRequest(ApiResponse<object>.Fail("Invalid date format. Use ISO 8601 format or YYYY-MM-DD."));
        }

        var result = await _eodService.GetHistoricalEodAsync(GetBranchId(), targetDate);
        if (result == null) return NotFound(ApiResponse<EodSnapshotDto>.Fail("No finalized EOD snapshot found for the specified date."));
        return Ok(ApiResponse<EodSnapshotDto>.Ok(result));
    }
}

public class FinalizeEodRequest
{
    public string? Date { get; set; }
}

