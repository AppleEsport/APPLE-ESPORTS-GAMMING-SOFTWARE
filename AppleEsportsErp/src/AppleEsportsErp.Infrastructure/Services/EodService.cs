using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Eod;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Infrastructure.Services;

public class EodService : IEodService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;

    public EodService(IUnitOfWork unitOfWork, IAuditService auditService)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
    }

    public async Task<ValidationStatusDto> GetValidationStatusAsync(Guid branchId, DateTimeOffset targetDate)
    {
        // startOfDay stays as the day's KEY - it is what a saved EOD snapshot is filed under,
        // and changing it would orphan every snapshot already finalised.
        var startOfDay = new DateTimeOffset(targetDate.UtcDateTime.Date, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1);

        // The window everything is actually counted over: 06:00 to 06:00 IST, the trading day.
        // A session that starts at 01:00 belongs to the night before, and the cash desk and
        // wallet desk have always read it that way. This screen read midnight-to-midnight UTC,
        // which is 05:30 IST - so late-night takings landed on the wrong day here and the right
        // day everywhere else, and the two screens disagreed about the same money.
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRange(DateOnly.FromDateTime(startOfDay.UtcDateTime.Date));

        var blockers = new List<string>();

        // 1. Check for unclosed Shifts
        var unclosedShifts = await _unitOfWork.Repository<Shift>().Query()
            .Where(s => s.BranchId == branchId && s.Status != ShiftStatus.Completed)
            .CountAsync();
            
        if (unclosedShifts > 0)
            blockers.Add($"{unclosedShifts} shift(s) are not yet Completed/Closed.");

        // 2. Check for unclosed Cash Registers
        var unclosedRegisters = await _unitOfWork.Repository<CashRegister>().Query()
            .Where(r => r.BranchId == branchId && r.Status != CashRegisterStatus.Closed)
            .CountAsync();

        if (unclosedRegisters > 0)
            blockers.Add($"{unclosedRegisters} cash register(s) have not finalized end-of-shift verification.");

        // 3. Check for Pending/Unpaid Bills
        var pendingBills = await _unitOfWork.Repository<Bill>().Query()
            .Where(b => b.BranchId == branchId && b.CreatedAt >= dayStart && b.CreatedAt < dayEnd && b.Status != BillStatus.Completed)
            .CountAsync();

        if (pendingBills > 0)
            blockers.Add($"{pendingBills} bill(s) are still pending/unpaid.");

        // 4. Check if Snapshot already exists
        var snapshotExists = await _unitOfWork.Repository<EodSnapshot>().Query()
            .AnyAsync(e => e.BranchId == branchId && e.ReportDate == startOfDay);
            
        if (snapshotExists)
            blockers.Add("End of Day has already been finalized for this date.");

        return new ValidationStatusDto
        {
            IsReady = !blockers.Any(),
            Blockers = blockers
        };
    }

    public async Task<EodReportDto> GenerateEodReportAsync(Guid branchId, DateTimeOffset targetDate)
    {
        // startOfDay stays as the day's KEY - it is what a saved EOD snapshot is filed under,
        // and changing it would orphan every snapshot already finalised.
        var startOfDay = new DateTimeOffset(targetDate.UtcDateTime.Date, TimeSpan.Zero);
        var endOfDay = startOfDay.AddDays(1);

        // The window everything is actually counted over: 06:00 to 06:00 IST, the trading day.
        // A session that starts at 01:00 belongs to the night before, and the cash desk and
        // wallet desk have always read it that way. This screen read midnight-to-midnight UTC,
        // which is 05:30 IST - so late-night takings landed on the wrong day here and the right
        // day everywhere else, and the two screens disagreed about the same money.
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRange(DateOnly.FromDateTime(startOfDay.UtcDateTime.Date));

        // Fetch Bills
        var bills = await _unitOfWork.Repository<Bill>().Query()
            .Where(b => b.BranchId == branchId && b.Status == BillStatus.Completed && b.CompletedAt >= dayStart && b.CompletedAt < dayEnd)
            .ToListAsync();
        var completedBills = bills;

        // Fetch Payments
        var payments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.BranchId == branchId && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
            .ToListAsync();

        // Fetch Registers
        // Selected by the trading day the register itself belongs to, which is the day it was
        // opened under. The register table already records that, and it is the same 06:00-06:00
        // day the money is counted by everywhere else.
        //
        // What was here before ended with "|| r.Status == CashRegisterStatus.Open", with no
        // date on it. That pulled in every register still open on any day in history. On the
        // live system that was 21 registers going back three weeks, and the day's opening
        // balance read Rs 5,500 against a drawer that had Rs 100 in it. A register left open
        // by a crash is a problem for the day it belongs to, never for today.
        var businessDay = DateOnly.FromDateTime(startOfDay.UtcDateTime.Date);

        var registers = await _unitOfWork.Repository<CashRegister>().Query()
            .Include(r => r.Operator)
            .Include(r => r.CashTransactions)
            .Where(r => r.BranchId == branchId && r.BusinessDay == businessDay)
            .OrderBy(r => r.OpenedAt)
            .ToListAsync();

        var walletTxs = await _unitOfWork.Repository<WalletTransaction>().Query()
            .Where(w => w.BranchId == branchId && w.CreatedAt >= dayStart && w.CreatedAt < dayEnd)
            .ToListAsync();

        var report = new EodReportDto
        {
            BranchId = branchId,
            ReportDate = startOfDay,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        // Fetch Credits (for deduction from revenue)
        var credits = await _unitOfWork.Repository<CustomerCredit>().Query()
            .Where(c => c.BranchId == branchId && ((c.CreatedAt >= dayStart && c.CreatedAt < dayEnd) || (c.ClearedAt >= dayStart && c.ClearedAt < dayEnd)))
            .ToListAsync();

        var pendingCredits = credits.Where(c => c.Status == "pending").Sum(c => c.CreditAmount);

        // Revenue (Gaming / Food)
        report.Revenue.TotalGamingRevenue = completedBills.Sum(b => b.GamingAmount);
        report.Revenue.TotalFoodRevenue = completedBills.Sum(b => b.FoodAmount);
        report.Revenue.TotalDiscounts = completedBills.Sum(b => b.DiscountAmount);
        report.Revenue.NetRevenue = completedBills.Sum(b => b.TotalAmount) - pendingCredits;

        // Payment Methods
        report.PaymentMethods.TotalCash = payments.Sum(p => p.CashAmount);
        report.PaymentMethods.TotalOnline = payments.Sum(p => p.OnlineAmount);
        report.PaymentMethods.TotalWalletDeductions = payments.Sum(p => p.WalletAmount);

        var topUps = walletTxs.Where(w => w.Action == WalletAction.Recharge).ToList();
        report.PaymentMethods.TotalWalletTopUps = topUps.Sum(w => w.Amount);

        // Split by how the top-up was actually paid, because the summary screen was adding the
        // whole figure into its cash line - so a UPI top-up inflated the cash the operator was
        // expected to be holding, and they came up short by exactly the amount nobody had
        // handed them.
        report.PaymentMethods.TotalWalletTopUpsCash = topUps.Sum(w => w.CashAmount);
        report.PaymentMethods.TotalWalletTopUpsOnline = topUps.Sum(w => w.OnlineAmount);

        // What the shop gave away in promotional bonus. Never money in; the owner is buying
        // loyalty with it, and it appeared nowhere at all before.
        report.PaymentMethods.TotalWalletBonusGiven = topUps.Sum(w => w.BonusAmount);

        // Every rupee that genuinely arrived today.
        //
        // Wallet deductions are deliberately absent. They are members spending balance that was
        // collected when they topped up, which may have been weeks ago - and if it was today,
        // it is already in the top-up figure. The screen was adding both, so a Rs 500 top-up
        // followed by Rs 90 of play reported Rs 590 of takings on a day Rs 500 came in.
        report.PaymentMethods.TotalCollected =
            report.PaymentMethods.TotalCash
            + report.PaymentMethods.TotalOnline
            + report.PaymentMethods.TotalWalletTopUpsCash
            + report.PaymentMethods.TotalWalletTopUpsOnline;

        // ── Does what was billed agree with what was taken ────────────────────────
        //
        // The two halves of the payment summary were shown side by side under one total with
        // no arithmetic joining them, so they simply disagreed whenever a discount was given or
        // a customer left owing money, and nothing said whether that was normal.
        var creditGivenToday = credits
            .Where(c => c.CreatedAt >= dayStart && c.CreatedAt < dayEnd)
            .Sum(c => c.CreditAmount);

        var creditClearedToday = credits
            .Where(c => c.ClearedAt >= dayStart && c.ClearedAt < dayEnd)
            .Sum(c => c.CreditAmount);

        report.Reconciliation.GrossBilled =
            report.Revenue.TotalGamingRevenue + report.Revenue.TotalFoodRevenue;
        report.Reconciliation.Discounts = report.Revenue.TotalDiscounts;
        report.Reconciliation.CreditGivenToday = creditGivenToday;
        report.Reconciliation.CreditClearedToday = creditClearedToday;

        report.Reconciliation.ShouldHaveBeenCollected =
            report.Reconciliation.GrossBilled
            - report.Reconciliation.Discounts
            - creditGivenToday
            + creditClearedToday;

        // Only what settled bills - top-ups are not bill payments and belong on the other side.
        report.Reconciliation.ActuallySettled =
            report.PaymentMethods.TotalCash
            + report.PaymentMethods.TotalOnline
            + report.PaymentMethods.TotalWalletDeductions;

        report.Reconciliation.Difference =
            report.Reconciliation.ActuallySettled - report.Reconciliation.ShouldHaveBeenCollected;

        // ── Cash Summary ──────────────────────────────────────────────────────────
        //
        // A branch has ONE physical drawer. Every figure here describes that one drawer over
        // the day, so nothing may be summed across shifts: shift 2 opens with the cash shift 1
        // left behind, and adding both totals counts the same notes twice. Summing them is
        // what produced an expected drawer of Rs 52,210 against a real one holding Rs 6,760.
        var firstRegister = registers.FirstOrDefault();
        var lastRegister = registers.LastOrDefault();

        // The money the drawer started the day with - the first shift's opening float, not the
        // sum of every shift's opening.
        report.Cash.TotalOpeningBalance = firstRegister?.OpeningBalance ?? 0m;

        // Only the part of each top-up that was actually paid in notes. TotalWalletTopUps is
        // every top-up whatever the method, which is right for revenue and wrong for a drawer:
        // adding a UPI top-up to the cash line inflates the cash the operator is expected to
        // have, and they show short by exactly the amount nobody ever handed them.
        var cashFromTopUps = walletTxs
            .Where(w => w.Action == WalletAction.Recharge)
            .Sum(w => w.CashAmount);

        report.Cash.TotalCashSales = registers.Sum(r => r.TotalCashSales) + cashFromTopUps;

        var allCashTxs = registers.SelectMany(r => r.CashTransactions).ToList();
        report.Cash.TotalCashInwards = allCashTxs.Where(t => t.TransactionType == "inward").Sum(t => t.CashAmount);
        report.Cash.TotalPettyExpenses = allCashTxs.Where(t => t.TransactionType == "petty_expense").Sum(t => Math.Abs(t.CashAmount));

        // Cash taken out of the drawer and handed to the owner. This leaves the branch, so it
        // must come off what the drawer is expected to hold - otherwise every handover looks
        // like the operator is short by exactly the amount they correctly sent up.
        report.Cash.TotalOwnerWithdrawals = allCashTxs.Where(t => t.TransactionType == "withdrawal").Sum(t => Math.Abs(t.CashAmount));

        // The drawer as it stands, what it was counted at, and the difference between the two -
        // all three read off the SAME register.
        //
        // That is the fix, and it matters because a trading day can now hold several drawers in
        // sequence: a shift handover closes one with a count and opens the next from what was
        // counted. This used to take "expected" from the drawer in use and "counted" from
        // whichever earlier drawer had last been counted, which printed three figures that could
        // not all be true at once - expected Rs 5,000, counted Rs 5,000, short Rs 240 - and left
        // an operator no way to tell which of them to believe.
        //
        // ExpectedDrawerCash is maintained on the register by every cash movement as it happens,
        // so the latest register carries the running figure. Re-deriving it is where double
        // counting creeps in.
        report.Cash.ExpectedCashInDrawer = lastRegister?.ExpectedDrawerCash ?? 0m;
        report.Cash.ActualPhysicalCashCounted = lastRegister?.PhysicalCashCounted;
        report.Cash.TotalDiscrepancy = lastRegister?.PhysicalCashCounted is null
            ? null
            : lastRegister.PhysicalCashCounted - lastRegister.ExpectedDrawerCash;

        // Money that went missing, or turned up spare, on a drawer already closed today - a
        // handover counted short. Reported separately from the figure above because it belongs to
        // an earlier shift: the drawer in use started from what was physically counted, so it is
        // not short by this as well, and adding the two together would charge the same shortfall
        // to two shifts.
        //
        // It also closes the column's arithmetic. Opening plus takings less expenses is what the
        // drawer would hold had nothing gone astray; subtract what did, and the expected figure
        // beneath it follows. Without this line the two simply disagree by the missing amount and
        // the screen offers no account of why.
        report.Cash.DifferencesFoundEarlier = registers
            .Where(r => r.Id != lastRegister?.Id && r.CashDifference.HasValue)
            .Sum(r => r.CashDifference!.Value);

        // Shifts Summary
        report.Shifts.TotalShifts = registers.Count;
        foreach (var reg in registers)
        {
            report.Shifts.ShiftDetails.Add(new ShiftDetailDto
            {
                ShiftId = reg.ShiftId,
                OperatorId = reg.OperatorId,
                OperatorName = reg.Operator?.FullName ?? "Unknown",
                TotalSales = reg.TotalCashSales,
                CashDiscrepancy = reg.CashDifference ?? 0
            });
        }

        // Operational Stats
        report.Operations.TotalSessions = await _unitOfWork.Repository<Session>().Query().CountAsync(s => s.BranchId == branchId && s.StartTime >= dayStart && s.StartTime < dayEnd);
        report.Operations.TotalReservations = await _unitOfWork.Repository<Reservation>().Query().CountAsync(r => r.BranchId == branchId && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd);
        report.Operations.TotalFoodOrders = await _unitOfWork.Repository<FoodOrder>().Query().CountAsync(o => o.BranchId == branchId && o.CreatedAt >= dayStart && o.CreatedAt < dayEnd);
        report.Operations.NewMembersRegistered = await _unitOfWork.Repository<Member>().Query().CountAsync(m => m.HomeBranchId == branchId && m.CreatedAt >= dayStart && m.CreatedAt < dayEnd);

        // Credit Logs (reuse credits fetched earlier)
        report.CreditLogs = credits.Select(c => new EodCreditLogDto
        {
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
        }).ToList();

        return report;
    }

    public async Task<EodSnapshotDto> FinalizeEodAsync(Guid branchId, Guid operatorId, DateTimeOffset targetDate)
    {
        await _unitOfWork.BeginTransactionAsync();
        try
        {
            var startOfDay = new DateTimeOffset(targetDate.UtcDateTime.Date, TimeSpan.Zero);
            
            // 1. Verify Validation Status
            var status = await GetValidationStatusAsync(branchId, startOfDay);
            if (!status.IsReady)
            {
                throw new AppException("Cannot finalize EOD due to unresolved blockers: " + string.Join("; ", status.Blockers));
            }

            // 2. Generate dynamic report payload
            var report = await GenerateEodReportAsync(branchId, startOfDay);

            // 3. Serialize securely using JSON
            var jsonData = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = false });

            // 4. Create immutable snapshot
            var snapshot = new EodSnapshot
            {
                BranchId = branchId,
                ReportDate = startOfDay,
                GeneratedByOperatorId = operatorId,
                SnapshotVersion = 1,
                SchemaVersion = "B.8-v1",
                SnapshotData = jsonData,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _unitOfWork.Repository<EodSnapshot>().AddAsync(snapshot);

            // 5. Audit Log
            await _auditService.LogAsync(new AuditEntry
            {
                OperatorId = operatorId,
                UserRole = "SuperAdmin", // Must be super admin
                UserName = "System",
                Action = "eod_finalize",
                BranchId = branchId,
                TargetType = "eod_snapshot",
                TargetId = snapshot.Id,
                Details = new { SchemaVersion = snapshot.SchemaVersion, Revenue = report.Revenue.NetRevenue }
            });

            await _unitOfWork.CommitTransactionAsync();

            return new EodSnapshotDto
            {
                Id = snapshot.Id,
                BranchId = snapshot.BranchId,
                ReportDate = snapshot.ReportDate,
                GeneratedByOperatorId = snapshot.GeneratedByOperatorId,
                SnapshotVersion = snapshot.SnapshotVersion,
                SchemaVersion = snapshot.SchemaVersion,
                CreatedAt = snapshot.CreatedAt,
                Data = report
            };
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<EodSnapshotDto?> GetHistoricalEodAsync(Guid branchId, DateTimeOffset targetDate)
    {
        var startOfDay = new DateTimeOffset(targetDate.UtcDateTime.Date, TimeSpan.Zero);
        var snapshot = await _unitOfWork.Repository<EodSnapshot>().Query()
            .FirstOrDefaultAsync(e => e.BranchId == branchId && e.ReportDate == startOfDay);

        if (snapshot == null) return null;

        var deserializedData = JsonSerializer.Deserialize<EodReportDto>(snapshot.SnapshotData);

        return new EodSnapshotDto
        {
            Id = snapshot.Id,
            BranchId = snapshot.BranchId,
            ReportDate = snapshot.ReportDate,
            GeneratedByOperatorId = snapshot.GeneratedByOperatorId,
            SnapshotVersion = snapshot.SnapshotVersion,
            SchemaVersion = snapshot.SchemaVersion,
            CreatedAt = snapshot.CreatedAt,
            Data = deserializedData!
        };
    }
}
