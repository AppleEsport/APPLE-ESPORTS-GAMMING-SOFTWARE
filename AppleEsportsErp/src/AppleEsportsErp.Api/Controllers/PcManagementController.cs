using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.PcManagement;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Infrastructure.Services;
using System.Security.Claims;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/pc-management")]
[Authorize] // Methods restricted individually
public class PcManagementController : ControllerBase
{
    private readonly IPcManagementService _pcManagementService;
    private readonly IMaintenanceLogService _maintenanceLogService;

    public PcManagementController(
        IPcManagementService pcManagementService,
        IMaintenanceLogService maintenanceLogService)
    {
        _pcManagementService = pcManagementService;
        _maintenanceLogService = maintenanceLogService;
    }

    private Guid GetSuperAdminId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

    [HttpPost("{pcId:guid}/maintenance")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin + "," + Roles.Operator)]
    public async Task<IActionResult> MarkMaintenance(Guid pcId, [FromQuery] bool enable)
    {
        var result = await _pcManagementService.MarkMaintenanceAsync(pcId, GetSuperAdminId(), enable);
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
    [HttpPost("maintenance-logs/mark")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> MarkMaintenance([FromBody] MarkMaintenanceDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
                return BadRequest(new { error = "Reason is required when marking PC for maintenance" });

            // Get operator ID from token
            var operatorIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(operatorIdString, out var operatorId))
                return Unauthorized(new { error = "Operator ID not found in token" });

            // Log the maintenance event
            await _maintenanceLogService.LogMaintenanceAsync(dto.PcId, dto.BranchId, operatorId, dto.Reason);

            // Also mark the PC as under maintenance (changes its state)
            await _pcManagementService.MarkMaintenanceAsync(dto.PcId, GetSuperAdminId(), true);

            return Ok(new { success = true, message = "PC marked for maintenance" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to mark maintenance" });
        }
    }

    [HttpPost("maintenance-logs/resolve/{pcId:guid}")]
    [Authorize(Policy = "OperatorOrAdmin")]
    public async Task<IActionResult> ResolveMaintenance(Guid pcId, [FromBody] ResolveMaintenanceDto? dto = null)
    {
        try
        {
            // Find active maintenance log for this PC
            var activeMaintenance = await _maintenanceLogService.GetActiveMaintenanceAsync(pcId);
            if (activeMaintenance != null)
            {
                // Resolve the maintenance log entry
                await _maintenanceLogService.ResolveMaintenanceAsync(activeMaintenance.Id, dto?.ResolutionNotes);
            }

            // Restore PC from maintenance (changes its state back to Idle)
            await _pcManagementService.MarkMaintenanceAsync(pcId, GetSuperAdminId(), false);

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

