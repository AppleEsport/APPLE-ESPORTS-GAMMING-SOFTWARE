using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.PcManagement;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/pc-management")]
[Authorize] // Methods restricted individually
public class PcManagementController : ControllerBase
{
    private readonly IPcManagementService _pcManagementService;
    private readonly IMaintenanceLogService _maintenanceLogService;
    private readonly AppleEsportsErp.Api.Services.IRemoteBranchControl _remote;
    private readonly AppleEsportsErp.Infrastructure.Data.AppDbContext _db;

    public PcManagementController(
        IPcManagementService pcManagementService,
        IMaintenanceLogService maintenanceLogService,
        AppleEsportsErp.Api.Services.IRemoteBranchControl remote,
        AppleEsportsErp.Infrastructure.Data.AppDbContext db)
    {
        _pcManagementService = pcManagementService;
        _maintenanceLogService = maintenanceLogService;
        _remote = remote;
        _db = db;
    }

    private Guid GetSuperAdminId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string GetActorRole() => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet("branch/{branchId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> GetPcsByBranch(Guid branchId, [FromQuery] bool includeDeleted = false)
    {
        var result = await _pcManagementService.GetPcsByBranchAsync(branchId, includeDeleted);
        return Ok(ApiResponse<List<PcDto>>.Ok(result));
    }

    [HttpPost("branch/{branchId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> AddPc(Guid branchId, [FromBody] CreatePcDto dto)
    {
        var result = await _pcManagementService.AddPcAsync(branchId, GetSuperAdminId(), dto);
        return Ok(ApiResponse<PcDto>.Ok(result));
    }

    [HttpPut("{pcId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> UpdatePc(Guid pcId, [FromBody] UpdatePcDto dto)
    {
        var result = await _pcManagementService.UpdatePcAsync(pcId, GetSuperAdminId(), dto);
        return Ok(ApiResponse<PcDto>.Ok(result));
    }

    [HttpPost("{pcId:guid}/transfer/{newBranchId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> TransferPc(Guid pcId, Guid newBranchId)
    {
        var result = await _pcManagementService.TransferPcAsync(pcId, newBranchId, GetSuperAdminId());
        return Ok(ApiResponse<PcDto>.Ok(result));
    }

    /// <summary>
    /// Takes a PC out of service, or puts it back.
    ///
    /// From Head Office this travels to the branch instead of being written here, for a reason
    /// that is specific to PC state rather than general squeamishness: the branch reports the
    /// state of every one of its PCs in each heartbeat, three seconds apart, and Head Office
    /// takes the branch's word for it. So a maintenance flag set here was overwritten by the
    /// very next beat - it appeared to work, then silently undid itself, and the machine that
    /// was supposed to be out of service went on taking customers.
    ///
    /// Sent down, the branch sets it, and the heartbeat that follows reports maintenance
    /// because maintenance is now the truth there. Nothing to overwrite.
    /// </summary>
    [HttpPost("{pcId:guid}/maintenance")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin + "," + Roles.Operator)]
    public async Task<IActionResult> MarkMaintenance(Guid pcId, [FromQuery] bool enable, CancellationToken ct)
    {
        if (_remote.MustTravel)
        {
            var branchId = await _db.Pcs.AsNoTracking()
                .Where(p => p.Id == pcId).Select(p => p.BranchId).FirstOrDefaultAsync(ct);

            if (branchId == Guid.Empty)
                return NotFound(ApiResponse<object>.Fail("Head Office has no such PC.", "PC_NOT_FOUND"));

            var receipt = await _remote.SendAsync(branchId, AppleEsportsErp.Api.Services.BranchCommands.SetPcState, new
            {
                pcId,
                state = enable ? "maintenance" : "idle",
            }, GetSuperAdminId(), ct);

            return Accepted(ApiResponse<object>.Ok(new
            {
                queued = true,
                commandId = receipt.CommandId,
                branchIsReporting = receipt.BranchIsReporting,
                message = receipt.Message,
            }));
        }

        var result = await _pcManagementService.MarkMaintenanceAsync(pcId, GetSuperAdminId(), GetActorRole(), enable);
        return Ok(ApiResponse<PcDto>.Ok(result));
    }

    [HttpDelete("{pcId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> DeletePc(Guid pcId)
    {
        await _pcManagementService.DeletePcAsync(pcId, GetSuperAdminId());
        return Ok(ApiResponse.Ok());
    }

    // Maintenance Logs Endpoints
    //
    // This is the pair the UI actually calls (client/src/api/maintenanceLogs.api.js), and until
    // now neither travelled. The routed endpoint above - POST {pcId}/maintenance - was correct
    // and unused, so flagging a PC from Head Office wrote Head Office's own copy, the branch was
    // never told, and a machine taken out of service remained sellable to a walk-in at the
    // counter. The reverse direction always worked, which is why it looked like a one-way sync
    // fault rather than a screen calling the wrong endpoint.
    [HttpPost("maintenance-logs/mark")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> MarkMaintenance([FromBody] MarkMaintenanceDto dto, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest(new { error = "Reason is required when marking PC for maintenance" });

            if (_remote.MustTravel)
            {
                // Taken from the PC rather than the body: the PC belongs to exactly one shop, so
                // this cannot send "take PC-04 down" to a branch that has no PC-04.
                var branchId = await _db.Pcs.AsNoTracking()
                    .Where(p => p.Id == dto.PcId).Select(p => p.BranchId).FirstOrDefaultAsync(ct);

                if (branchId == Guid.Empty)
                    return NotFound(new { error = "Head Office has no such PC." });

                var receipt = await _remote.SendAsync(branchId, AppleEsportsErp.Api.Services.BranchCommands.SetMaintenance, new
                {
                    pcId = dto.PcId,
                    enable = true,
                    reason = dto.Reason,
                }, GetSuperAdminId(), ct);

                return Accepted(new
                {
                    success = true,
                    queued = true,
                    commandId = receipt.CommandId,
                    branchIsReporting = receipt.BranchIsReporting,
                    message = receipt.Message,
                });
            }

            // Get operator ID from token
            var operatorIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(operatorIdString, out var operatorId))
                return Unauthorized(new { error = "Operator ID not found in token" });

            // Log the maintenance event
            await _maintenanceLogService.LogMaintenanceAsync(dto.PcId, dto.BranchId, operatorId, GetActorRole(), dto.Reason);

            // Also mark the PC as under maintenance (changes its state)
            await _pcManagementService.MarkMaintenanceAsync(dto.PcId, operatorId, GetActorRole(), true);

            return Ok(new { success = true, message = "PC marked for maintenance" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to mark maintenance" });
        }
    }

    [HttpPost("maintenance-logs/resolve/{pcId:guid}")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> ResolveMaintenance(Guid pcId, [FromBody] ResolveMaintenanceDto? dto = null, CancellationToken ct = default)
    {
        try
        {
            if (_remote.MustTravel)
            {
                var branchId = await _db.Pcs.AsNoTracking()
                    .Where(p => p.Id == pcId).Select(p => p.BranchId).FirstOrDefaultAsync(ct);

                if (branchId == Guid.Empty)
                    return NotFound(new { error = "Head Office has no such PC." });

                var receipt = await _remote.SendAsync(branchId, AppleEsportsErp.Api.Services.BranchCommands.SetMaintenance, new
                {
                    pcId,
                    enable = false,
                    notes = dto?.ResolutionNotes,
                }, GetSuperAdminId(), ct);

                return Accepted(new
                {
                    success = true,
                    queued = true,
                    commandId = receipt.CommandId,
                    branchIsReporting = receipt.BranchIsReporting,
                    message = receipt.Message,
                });
            }

            var operatorIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(operatorIdString, out var operatorId))
                return Unauthorized(new { error = "Operator ID not found in token" });

            // Find active maintenance log for this PC
            var activeMaintenance = await _maintenanceLogService.GetActiveMaintenanceAsync(pcId);
            if (activeMaintenance != null)
            {
                // Resolve the maintenance log entry
                await _maintenanceLogService.ResolveMaintenanceAsync(activeMaintenance.Id, operatorId, GetActorRole(), dto?.ResolutionNotes);
            }

            // Restore PC from maintenance (changes its state back to Idle)
            await _pcManagementService.MarkMaintenanceAsync(pcId, operatorId, GetActorRole(), false);

            return Ok(new { success = true, message = "PC restored from maintenance" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to resolve maintenance" });
        }
    }

    [HttpGet("maintenance-logs/branch/{branchId:guid}")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> GetBranchMaintenanceLogs(Guid branchId, [FromQuery] int days = 7)
    {
        try
        {
            var logs = await _maintenanceLogService.GetBranchMaintenanceLogsAsync(branchId, days);
            return Ok(new { success = true, data = logs });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to retrieve maintenance logs" });
        }
    }
}

