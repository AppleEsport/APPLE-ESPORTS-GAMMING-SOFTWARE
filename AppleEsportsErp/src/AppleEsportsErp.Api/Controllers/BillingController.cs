using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.DTOs.Billing;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Application.Interfaces;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/bills")]
[Authorize]
[BranchIsolation]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly IHubContext<AppleEsportsErp.Api.Hubs.PcOverlayHub> _pcOverlayHub;
    private readonly AppleEsportsErp.Infrastructure.Data.AppDbContext _db;
    private readonly AppleEsportsErp.Api.Services.IRemoteBranchControl _remote;

    public static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, AppleEsportsErp.Application.DTOs.Billing.PendingWalletApproval> PendingApprovals = new();

    public BillingController(
        IBillingService billingService,
        IHubContext<AppleEsportsErp.Api.Hubs.PcOverlayHub> pcOverlayHub,
        AppleEsportsErp.Infrastructure.Data.AppDbContext db,
        AppleEsportsErp.Api.Services.IRemoteBranchControl remote)
    {
        _billingService = billingService;
        _pcOverlayHub = pcOverlayHub;
        _db = db;
        _remote = remote;
    }

    private Guid GetBranchId() => Guid.Parse(HttpContext.Items["BranchId"]!.ToString()!);

    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /// <summary>
    /// Turns "Head Office pressed Pay" into an instruction for the branch that actually has the
    /// customer, the till and the cash - same reasoning as SessionsController.SendToBranchAsync.
    /// A payment recorded only at Head Office is invisible to the counter: the register is never
    /// credited, and the PC stays locked showing Billing forever, no matter what Head Office's
    /// own copy says.
    /// </summary>
    private async Task<IActionResult> SendToBranchAsync(
        Guid branchId, string commandType, object payload, CancellationToken ct)
    {
        var receipt = await _remote.SendAsync(branchId, commandType, payload, CurrentUserId(), ct);

        return Accepted(ApiResponse<object>.Ok(new
        {
            queued = true,
            commandId = receipt.CommandId,
            branchIsReporting = receipt.BranchIsReporting,
            message = receipt.Message,
        }));
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveBills([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var result = await _billingService.GetActiveBillsAsync(GetBranchId(), page, pageSize);
        return Ok(ApiResponse<PaginatedResult<BillDto>>.Ok(result));
    }

    [HttpGet("deferred")]
    public async Task<IActionResult> GetDeferredBills()
    {
        var result = await _billingService.GetDeferredBillsAsync(GetBranchId());
        return Ok(new { success = true, data = result });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBill(Guid id)
    {
        var result = await _billingService.GetBillAsync(GetBranchId(), id);
        return Ok(ApiResponse<BillDto>.Ok(result));
    }

    [HttpGet("by-number/{billNumber}")]
    public async Task<IActionResult> GetBillByNumber(string billNumber)
    {
        var result = await _billingService.GetBillByNumberAsync(GetBranchId(), billNumber);
        return Ok(ApiResponse<BillDto>.Ok(result));
    }

    [HttpPost("{id:guid}/discount")]
    [Idempotent]
    [Authorize] // Replaced strict policy with inline check
    public async Task<IActionResult> ApplyDiscount(Guid id, [FromBody] ApplyDiscountDto dto, CancellationToken ct)
    {
        var role = User.FindFirstValue(ClaimTypes.Role);
        var permissionsStr = User.FindFirstValue("dashboardPermissions");

        bool canDiscount = role == AppleEsportsErp.Application.Constants.Roles.SuperAdmin;
        if (!canDiscount && role == AppleEsportsErp.Application.Constants.Roles.Admin && !string.IsNullOrEmpty(permissionsStr))
        {
            try
            {
                var permissions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(permissionsStr);
                if (permissions != null && permissions.TryGetValue("discount", out var hasDiscount) && hasDiscount)
                {
                    canDiscount = true;
                }
            }
            catch { }
        }

        if (!canDiscount) return Forbid();

        var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // Same reasoning as payment: a discount is a change to money the branch's own bill
        // is holding, and only the branch's copy is the one its counter and register read.
        // BillingService.ApplyDiscountAsync refuses to write this directly at Head Office -
        // this is the other half of that fix, actually sending it somewhere useful instead
        // of just failing.
        if (_remote.MustTravel)
        {
            var branchId = await _db.Set<AppleEsportsErp.Domain.Entities.Bill>().AsNoTracking()
                .Where(b => b.Id == id).Select(b => b.BranchId).FirstOrDefaultAsync(ct);

            if (branchId == Guid.Empty)
                return NotFound(ApiResponse<object>.Fail("Head Office has no such bill.", "BILL_NOT_FOUND"));

            return await SendToBranchAsync(branchId, AppleEsportsErp.Api.Services.BranchCommands.ApplyDiscount, new
            {
                billId = id,
                dto.DiscountType,
                dto.DiscountValue,
                dto.Reason,
                actorId = adminId,
                actorRole = role,
            }, ct);
        }

        var result = await _billingService.ApplyDiscountAsync(GetBranchId(), adminId, role!, id, dto);
        return Ok(ApiResponse<BillDto>.Ok(result));
    }

    [HttpPost("{id:guid}/pay")]
    [Idempotent]
    public async Task<IActionResult> ProcessPayment(Guid id, [FromBody] ProcessPaymentDto dto, CancellationToken ct)
    {
        // From Head Office this becomes an instruction for the branch, exactly like Stop
        // Session - a payment written only into Head Office's synced copy leaves the branch's
        // own register uncredited and its PC locked on Billing forever, since the branch was
        // never actually told the customer paid.
        if (_remote.MustTravel)
        {
            var branchId = await _db.Set<AppleEsportsErp.Domain.Entities.Bill>().AsNoTracking()
                .Where(b => b.Id == id).Select(b => b.BranchId).FirstOrDefaultAsync(ct);

            if (branchId == Guid.Empty)
                return NotFound(ApiResponse<object>.Fail("Head Office has no such bill.", "BILL_NOT_FOUND"));

            return await SendToBranchAsync(branchId, AppleEsportsErp.Api.Services.BranchCommands.ProcessPayment, new
            {
                billId = id,
                dto.PaymentType,
                dto.CashAmount,
                dto.OnlineAmount,
                dto.WalletAmount,
                dto.CashReceived,
                dto.CreditAmount,
                dto.MemberId,
                dto.CustomerName,
                dto.CustomerPhone,
            }, ct);
        }

        var result = await _billingService.ProcessPaymentAsync(GetBranchId(), (await this.GetOperatorIdAsync()), (await this.GetShiftIdAsync()), id, dto);
        return Ok(ApiResponse<BillDto>.Ok(result));
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> RemoveBillItem(Guid id, Guid itemId)
    {
        var result = await _billingService.RemoveBillItemAsync(GetBranchId(), (await this.GetOperatorIdAsync()), id, itemId);
        return Ok(ApiResponse<BillDto>.Ok(result));
    }

    [HttpPost("{id:guid}/request-wallet-approval")]
    public async Task<IActionResult> RequestWalletApproval(Guid id)
    {
        var bill = await _billingService.GetBillAsync(GetBranchId(), id);
        if (bill == null || bill.MemberId == null)
            return BadRequest(new { success = false, error = "Bill not found or not linked to a member." });

        if (bill.PcId == null)
            return BadRequest(new { success = false, error = "No PC associated with this bill." });

        var approvalToken = Guid.NewGuid();
        var pendingRequest = new AppleEsportsErp.Application.DTOs.Billing.PendingWalletApproval
        {
            BillId = id,
            OperatorId = await this.GetOperatorIdAsync(),
            ShiftId = await this.GetShiftIdAsync(),
            BranchId = GetBranchId(),
            Amount = bill.TotalAmount
        };

        PendingApprovals.TryAdd(approvalToken, pendingRequest);

        // Send to PC
        await _pcOverlayHub.Clients.Group($"pc:{bill.PcId}").SendAsync("ReceiveWalletApprovalRequest", new
        {
            billId = id,
            amount = bill.TotalAmount,
            approvalToken = approvalToken
        });

        // Also fallback to pcNumber if group doesn't use ID
        if (!string.IsNullOrEmpty(bill.PcNumber))
        {
            await _pcOverlayHub.Clients.Group($"pc:{bill.PcNumber}").SendAsync("ReceiveWalletApprovalRequest", new
            {
                billId = id,
                amount = bill.TotalAmount,
                approvalToken = approvalToken
            });
        }

        return Ok(new { success = true, approvalToken });
    }
}

