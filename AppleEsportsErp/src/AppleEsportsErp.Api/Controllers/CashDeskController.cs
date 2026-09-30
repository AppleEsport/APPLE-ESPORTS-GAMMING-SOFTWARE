using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Cash;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Application.Interfaces;
using System.Security.Claims;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/cash-desk")]
[Authorize]
[BranchIsolation]
public class CashDeskController : ControllerBase
{
    private readonly ICashDeskService _cashDeskService;

    public CashDeskController(ICashDeskService cashDeskService)
    {
        _cashDeskService = cashDeskService;
    }

    private Guid GetBranchId() => Guid.Parse(HttpContext.Items["BranchId"]!.ToString()!);

    [HttpPost("verify-start")]
    [Idempotent]
    public async Task<IActionResult> StartVerification()
    {
        await this.EnsureNotSuperAdminForCashAsync();
        await _cashDeskService.StartVerificationAsync(GetBranchId(), (await this.GetOperatorIdAsync()), (await this.GetShiftIdAsync()));
        return Ok(new { success = true, message = "Verification started, register locked." });
    }

    [HttpPost("denominations")]
    [Idempotent]
    public async Task<IActionResult> SubmitDenominations([FromBody] SubmitDenominationDto dto)
    {
        await this.EnsureNotSuperAdminForCashAsync();
        var result = await _cashDeskService.SubmitDenominationsAsync(GetBranchId(), (await this.GetOperatorIdAsync()), (await this.GetShiftIdAsync()), dto);
        return Ok(ApiResponse<DenominationCountDto>.Ok(result));
    }

    [HttpPost("close/{registerId:guid}")]
    [Idempotent]
    public async Task<IActionResult> CloseRegister(Guid registerId, [FromBody] CloseCashRegisterDto? dto)
    {
        await this.EnsureNotSuperAdminForCashAsync();
        await _cashDeskService.CloseRegisterAsync(GetBranchId(), (await this.GetOperatorIdAsync()), (await this.GetShiftIdAsync()), registerId, dto ?? new CloseCashRegisterDto());
        return Ok(new { success = true, message = "Register closed successfully" });
    }

    [HttpPost("cancel-verification/{registerId:guid}")]
    [Idempotent]
    public async Task<IActionResult> CancelVerification(Guid registerId)
    {
        await this.EnsureNotSuperAdminForCashAsync();
        await _cashDeskService.CancelVerificationAsync(GetBranchId(), (await this.GetOperatorIdAsync()), (await this.GetShiftIdAsync()), registerId);
        return Ok(new { success = true, message = "Verification cancelled, register unlocked" });
    }

    /// <summary>
    /// The one deliberate exception to Super Admin's read-only cash rule - undoing an accidental
    /// "last shift of the day" tick is an administrative correction, not an operator cash action.
    /// </summary>
    [HttpPost("reopen-day")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.SuperAdmin}")]
    [Idempotent]
    public async Task<IActionResult> ReopenDay()
    {
        var name = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email) ?? "Admin";
        await _cashDeskService.ReopenLastDayCloseAsync(GetBranchId(), Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), name);
        return Ok(new { success = true, message = "Day close undone. The drawer is open again." });
    }
}


