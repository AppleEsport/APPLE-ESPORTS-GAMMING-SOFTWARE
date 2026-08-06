using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Application.Exceptions;

namespace AppleEsportsErp.Api.Extensions;

public static class ControllerExtensions
{
    public static async Task<Guid> GetOperatorIdAsync(this ControllerBase controller)
    {
        var user = controller.User;
        
        // If it's a regular operator, just return their ID from the JWT
        if (!user.IsInRole(Roles.SuperAdmin) && !user.IsInRole(Roles.Admin) && !user.IsInRole("Member"))
        {
            return Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }

        // SuperAdmin or Member is acting. Find or create a System Operator for the active branch.
        var branchIdStr = controller.HttpContext.Items["BranchId"]?.ToString();
        if (string.IsNullOrEmpty(branchIdStr))
            throw new InvalidOperationException("BranchId is not set in HttpContext. Cannot resolve System Operator.");

        var branchId = Guid.Parse(branchIdStr);
        var db = controller.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        var sysUsername = $"system_admin_{branchId:N}";
        var sysOp = await db.Operators.FirstOrDefaultAsync(o => o.BranchId == branchId && o.Username == sysUsername);
        if (sysOp == null)
        {
            sysOp = new Operator
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                FullName = "System Administrator",
                Username = sysUsername,
                Email = $"{sysUsername}@system.local",
                PasswordHash = "LOCKED",
                Status = OperatorStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Operators.Add(sysOp);
            await db.SaveChangesAsync();
        }

        return sysOp.Id;
    }

    public static async Task<Guid> GetShiftIdAsync(this ControllerBase controller)
    {
        var user = controller.User;
        var branchIdStr = controller.HttpContext.Items["BranchId"]?.ToString();
        if (string.IsNullOrEmpty(branchIdStr))
            throw new InvalidOperationException("BranchId is not set in HttpContext.");

        var branchId = Guid.Parse(branchIdStr);
        var db = controller.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        // For regular operators, try JWT claim first, then fallback to database
        if (!user.IsInRole(Roles.SuperAdmin) && !user.IsInRole(Roles.Admin) && !user.IsInRole("Member"))
        {
            var shiftClaim = user.FindFirstValue("shiftId");
            if (!string.IsNullOrEmpty(shiftClaim) && Guid.TryParse(shiftClaim, out var shiftGuid))
            {
                var shiftFromClaim = await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftGuid && s.Status == ShiftStatus.Active);
                if (shiftFromClaim != null)
                    return shiftGuid;
            }

            // Fallback: Get the operator's active shift from database
            var operatorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var operatorShift = await db.Shifts
                .Where(s => s.BranchId == branchId && s.OperatorId == operatorId && s.Status == ShiftStatus.Active)
                .OrderByDescending(s => s.LoginTime)
                .FirstOrDefaultAsync();

            if (operatorShift != null)
                return operatorShift.Id;

            // If no operator shift found, throw error with clear message
            throw new AppException("No active shift found for this operator. Please log in through the proper login flow.");
        }

        // For SuperAdmin/Admin, find or create active shift for branch
        var activeShift = await db.Shifts
            .Where(s => s.BranchId == branchId && s.Status == ShiftStatus.Active)
            .OrderByDescending(s => s.LoginTime)
            .FirstOrDefaultAsync();

        if (activeShift == null)
        {
            var sysOpId = await controller.GetOperatorIdAsync();
            activeShift = new Shift
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                OperatorId = sysOpId,
                LoginTime = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = ShiftStatus.Active
            };
            db.Shifts.Add(activeShift);

            var register = new CashRegister
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                OperatorId = sysOpId,
                ShiftId = activeShift.Id,
                OpeningBalance = 0,
                ExpectedDrawerCash = 0,
                TotalCashSales = 0,
                TotalSplitCash = 0,
                Status = CashRegisterStatus.Open,
                OpenedAt = DateTimeOffset.UtcNow
            };
            db.CashRegisters.Add(register);
            await db.SaveChangesAsync();
        }

        return activeShift.Id;
    }
}
