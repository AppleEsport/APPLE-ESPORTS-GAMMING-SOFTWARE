using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Interfaces;
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

        var endTime = shift.LogoutTime ?? DateTimeOffset.UtcNow;

        // Money belongs to the shift that TOOK it, not the shift that opened the bill.
        //
        // This used to select bills by their own CreatedAt/ShiftId and then read the payments
        // hanging off them. A session opened before midnight and settled after — routine at a
        // branch trading until 02:00 — put the payment on a bill belonging to the previous
        // shift, so the operator who actually collected the money saw none of it and came up
        // short at reconciliation. Querying payments directly by when they were taken is the
        // only reading that matches the cash drawer.
        var payments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.BranchId == branchId
                     && p.OnlineAmount > 0
                     && p.CreatedAt >= shift.LoginTime
                     && p.CreatedAt <= endTime)
            .Include(p => p.Bill)
                .ThenInclude(b => b.Member)
            .ToListAsync();

        var walletTxs = await _unitOfWork.Repository<WalletTransaction>().Query()
            .Where(w => w.BranchId == branchId && w.CreatedAt >= shift.LoginTime && w.CreatedAt <= endTime)
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

        var endTime = shift.LogoutTime ?? DateTimeOffset.UtcNow;

        var walletTxs = await _unitOfWork.Repository<WalletTransaction>().Query()
            .Where(w => w.BranchId == branchId && w.CreatedAt >= shift.LoginTime && w.CreatedAt <= endTime)
            .Include(w => w.Member)
            .ToListAsync();

        // Same correction as the Online Desk: attribute a payment to the shift that took it,
        // not to the shift the bill happened to be opened in.
        var walletPayments = await _unitOfWork.Repository<Payment>().Query()
            .Where(p => p.BranchId == branchId
                     && p.WalletAmount > 0
                     && p.CreatedAt >= shift.LoginTime
                     && p.CreatedAt <= endTime)
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
