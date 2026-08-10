using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Application.DTOs.SystemDesks;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Infrastructure.Services;

public class SystemDesksService : ISystemDesksService
{
    private readonly IUnitOfWork _unitOfWork;

    public SystemDesksService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OnlineDeskSummaryDto> GetActiveOnlineDeskAsync(Guid branchId, Guid shiftId)
    {
        var shift = await _unitOfWork.Repository<Shift>().Query()
            .FirstOrDefaultAsync(s => s.Id == shiftId && s.BranchId == branchId);

        if (shift == null)
            throw new Exception("Shift not found.");

        // Money is reported per TRADING DAY (06:00-06:00 IST), not per operator login.
        //
        // Two separate faults came from scoping it by shift. Bills were selected by their own
        // CreatedAt, so a session opened before midnight and settled after — routine at a
        // branch trading past 02:00 — landed on the previous shift and the operator who took
        // the money saw Rs 0. And an operator who logs in three times in a day used to split
        // one day's takings into three sets of figures that reconcile against nothing.
        //
        // How often somebody logs in is their business. The day's money is the day's money.
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRangeFor(DateTimeOffset.UtcNow);

        var payments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.BranchId == branchId
                     && p.OnlineAmount > 0
                     && p.CreatedAt >= dayStart
                     && p.CreatedAt < dayEnd)
            .Include(p => p.Bill)
                .ThenInclude(b => b.Member)
            .ToListAsync();

        var walletTxs = await _unitOfWork.Repository<WalletTransaction>().Query()
            .Where(w => w.BranchId == branchId && w.CreatedAt >= dayStart && w.CreatedAt < dayEnd)
            .Include(w => w.Member)
            .ToListAsync();

        var dto = new OnlineDeskSummaryDto
        {
            ShiftId = shiftId
        };

        foreach (var payment in payments)
        {
            dto.TotalOnlineSales += payment.OnlineAmount;
            dto.Transactions.Add(new OnlineTransactionDto
            {
                Id = payment.Id,
                Timestamp = payment.CreatedAt,
                Description = $"Bill Payment #{payment.Bill?.BillNumber} " +
                              $"({payment.Bill?.CustomerName ?? payment.Bill?.Member?.Username ?? "Walk-in"})",
                Amount = payment.OnlineAmount,
                PaymentMethod = "Online"
            });
        }

        foreach (var tx in walletTxs)
        {
            if (tx.OnlineAmount > 0)
            {
                dto.TotalOnlineSales += tx.OnlineAmount;
                dto.Transactions.Add(new OnlineTransactionDto
                {
                    Id = tx.Id,
                    Timestamp = tx.CreatedAt,
                    Description = $"Wallet {tx.Action} - {tx.TargetWallet} ({tx.Member?.Username ?? "Member"})",
                    Amount = tx.OnlineAmount,
                    PaymentMethod = "Online"
                });
            }
        }

        dto.Transactions = dto.Transactions.OrderByDescending(t => t.Timestamp).ToList();

        return dto;
    }

    public async Task<WalletDeskSummaryDto> GetActiveWalletDeskAsync(Guid branchId, Guid shiftId)
    {
        var shift = await _unitOfWork.Repository<Shift>().Query()
            .FirstOrDefaultAsync(s => s.Id == shiftId && s.BranchId == branchId);

        if (shift == null)
            throw new Exception("Shift not found.");


        // Same trading-day scope as the Online Desk — see the note there.
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRangeFor(DateTimeOffset.UtcNow);

        var walletTxs = await _unitOfWork.Repository<WalletTransaction>().Query()
            .Where(w => w.BranchId == branchId && w.CreatedAt >= dayStart && w.CreatedAt < dayEnd)
            .Include(w => w.Member)
            .ToListAsync();

        var walletPayments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.BranchId == branchId
                     && p.WalletAmount > 0
                     && p.CreatedAt >= dayStart
                     && p.CreatedAt < dayEnd)
            .Include(p => p.Bill)
                .ThenInclude(b => b.Member)
            .ToListAsync();

        var dto = new WalletDeskSummaryDto
        {
            ShiftId = shiftId
        };

        foreach (var tx in walletTxs)
        {
            if (tx.Action == WalletAction.Recharge)
            {
                dto.TotalWalletTopUps += tx.Amount;
            }
            else if (tx.Action == WalletAction.DeductionGaming || tx.Action == WalletAction.DeductionFood)
            {
                dto.TotalWalletDeductions += tx.Amount;
            }

            dto.Transactions.Add(new WalletTransactionSummaryDto
            {
                Id = tx.Id,
                Timestamp = tx.CreatedAt,
                Description = $"Wallet {tx.Action} - {tx.TargetWallet} ({tx.Member?.Username ?? "Member"}) " + (string.IsNullOrEmpty(tx.Reason) ? "" : $"({tx.Reason})"),
                Amount = tx.Amount,
                Action = tx.Action.ToString()
            });
        }

        foreach (var payment in walletPayments)
        {
            dto.TotalWalletDeductions += payment.WalletAmount;
            dto.Transactions.Add(new WalletTransactionSummaryDto
            {
                Id = payment.Id,
                Timestamp = payment.CreatedAt,
                Description = $"Bill Payment via Wallet #{payment.Bill?.BillNumber} " +
                              $"({payment.Bill?.CustomerName ?? payment.Bill?.Member?.Username ?? "Walk-in"})",
                Amount = payment.WalletAmount,
                Action = "Deduction"
            });
        }

        dto.Transactions = dto.Transactions.OrderByDescending(t => t.Timestamp).ToList();

        return dto;
    }
}
