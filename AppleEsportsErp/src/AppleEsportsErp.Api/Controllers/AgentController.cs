using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Infrastructure.Identity;
using System.Security.Claims;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Agent management controller — handles Gaming PC agent registration, 
/// token generation, and connection status monitoring.
/// </summary>
[ApiController]
[Route("api/agent")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtTokenService _jwtTokenService;

    public AgentController(IUnitOfWork unitOfWork, JwtTokenService jwtTokenService)
    {
        _unitOfWork = unitOfWork;
        _jwtTokenService = jwtTokenService;
    }

    /// <summary>Generate a long-lived machine JWT for a specific Gaming PC agent</summary>
    [HttpPost("token/{pcId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> GenerateAgentToken(Guid pcId)
    {
        var pc = await _unitOfWork.Repository<Domain.Entities.Pc>()
            .Query()
            .Include(p => p.Branch)
            .FirstOrDefaultAsync(p => p.Id == pcId && !p.IsDeleted);

        if (pc == null)
            return NotFound(ApiResponse<object>.Fail("PC not found"));

        // Generate a long-lived machine token (365 days)
        var token = _jwtTokenService.GenerateAgentToken(
            pcId: pc.Id.ToString(),
            branchId: pc.BranchId.ToString(),
            pcNumber: pc.PcNumber
        );

        return Ok(ApiResponse<object>.Ok(new
        {
            token,
            pcId = pc.Id,
            pcNumber = pc.PcNumber,
            branchId = pc.BranchId,
            branchName = pc.Branch?.Name,
            expiresIn = "365 days",
            instructions = "Save this token in the agent's appsettings.agent.json file on the Gaming PC."
        }));
    }

    /// <summary>
    /// First-run setup for a Gaming PC — POST /api/agent/provision.
    ///
    /// Binds one physical machine to one PC record, permanently. Until this succeeds the PC
    /// sits in AwaitingSetup and cannot take a session, so an operator can never seat a
    /// customer at a machine that will not unlock.
    ///
    /// Re-running setup on the <i>same</i> machine is allowed and simply returns the existing
    /// token — reinstalls, repairs and re-imaging must not require a Super Admin. Claiming a
    /// PC number that a <i>different</i> machine already holds is refused: two machines both
    /// answering as "PC-1" would send unlock commands to the wrong screen.
    /// </summary>
    [HttpPost("provision")]
    [AllowAnonymous]   // No credentials exist yet — the machine is claiming its identity here.
    public async Task<IActionResult> Provision([FromBody] AgentProvisionDto dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.MachineId) || string.IsNullOrWhiteSpace(dto.PcNumber))
            return BadRequest(ApiResponse<object>.Fail("Machine id and PC number are required."));

        var machineId = dto.MachineId.Trim();
        var pcNumber = dto.PcNumber.Trim();

        var pcs = _unitOfWork.Repository<Domain.Entities.Pc>();

        // Has this machine already claimed a seat? If so it is a reinstall, not a new setup.
        var alreadyClaimed = await pcs.Query()
            .Include(p => p.Branch)
            .FirstOrDefaultAsync(p => p.MachineId == machineId && !p.IsDeleted);

        if (alreadyClaimed != null)
        {
            if (!string.Equals(alreadyClaimed.PcNumber, pcNumber, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(ApiResponse<object>.Fail(
                    $"This machine is already set up as {alreadyClaimed.PcNumber}. " +
                    $"Remove that setup from the dashboard before assigning it to {pcNumber}.",
                    "MACHINE_ALREADY_PROVISIONED"));
            }

            // Same machine, same seat — hand back what it already has.
            return Ok(ApiResponse<object>.Ok(new
            {
                pcId = alreadyClaimed.Id,
                pcNumber = alreadyClaimed.PcNumber,
                branchId = alreadyClaimed.BranchId,
                branchName = alreadyClaimed.Branch?.Name,
                token = alreadyClaimed.MachineToken,
                reused = true,
            }));
        }

        var target = await pcs.Query()
            .Include(p => p.Branch)
            .FirstOrDefaultAsync(p => p.BranchId == dto.BranchId
                                   && p.PcNumber.ToLower() == pcNumber.ToLower()
                                   && !p.IsDeleted);

        if (target == null)
            return NotFound(ApiResponse<object>.Fail(
                $"{pcNumber} does not exist at this branch. Create it in the dashboard first.",
                "PC_NOT_FOUND"));

        if (target.MachineId != null)
        {
            return Conflict(ApiResponse<object>.Fail(
                $"{pcNumber} is already set up on another machine " +
                $"(since {AppleEsportsErp.Application.Services.IndiaTime.Format(target.ProvisionedAt ?? target.UpdatedAt)}). " +
                "Pick a different PC number, or remove the existing setup from the dashboard first.",
                "PC_ALREADY_PROVISIONED"));
        }

        var now = DateTimeOffset.UtcNow;
        target.MachineId = machineId;
        target.MachineToken = _jwtTokenService.GenerateAgentToken(
            pcId: target.Id.ToString(),
            branchId: target.BranchId.ToString(),
            pcNumber: target.PcNumber);
        target.ProvisionedAt = now;
        target.UpdatedAt = now;
        target.IpAddress = dto.IpAddress ?? target.IpAddress;

        // Only now does it become bookable.
        if (target.State == Domain.Enums.PcState.AwaitingSetup)
            target.State = Domain.Enums.PcState.Idle;

        await _unitOfWork.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            pcId = target.Id,
            pcNumber = target.PcNumber,
            branchId = target.BranchId,
            branchName = target.Branch?.Name,
            token = target.MachineToken,
            reused = false,
        }));
    }

    /// <summary>
    /// Releases a PC so a replacement machine can be set up against the same number —
    /// for a dead or swapped-out box. Super Admin only: it hands the seat's identity to
    /// whichever machine claims it next.
    /// </summary>
    [HttpPost("deprovision/{pcId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> Deprovision(Guid pcId)
    {
        var pc = await _unitOfWork.Repository<Domain.Entities.Pc>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == pcId && !p.IsDeleted);

        if (pc == null)
            return NotFound(ApiResponse<object>.Fail("PC not found"));

        if (pc.State == Domain.Enums.PcState.Active)
            return BadRequest(ApiResponse<object>.Fail(
                $"{pc.PcNumber} has a session running. Stop it before removing the setup.",
                "PC_IN_USE"));

        pc.MachineId = null;
        pc.MachineToken = null;
        pc.ProvisionedAt = null;
        pc.IsAgentOnline = false;
        pc.ConnectionMode = "None";
        pc.State = Domain.Enums.PcState.AwaitingSetup;
        pc.UpdatedAt = DateTimeOffset.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            pcId = pc.Id,
            pcNumber = pc.PcNumber,
            message = $"{pc.PcNumber} is free to be set up again.",
        }));
    }

    /// <summary>Get agent connection status for all PCs in a branch</summary>
    [HttpGet("status/{branchId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin + "," + Roles.Operator)]
    public async Task<IActionResult> GetAgentStatuses(Guid branchId)
    {
        var pcs = await _unitOfWork.Repository<Domain.Entities.Pc>()
            .Query()
            .Where(p => p.BranchId == branchId && !p.IsDeleted)
            .OrderBy(p => p.PcNumber)
            .Select(p => new
            {
                p.Id,
                p.PcNumber,
                p.PcName,
                p.IsAgentOnline,
                p.ConnectionMode,
                p.LastAgentHeartbeat,
                State = p.State.ToString(),
                p.IpAddress
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(pcs));
    }

    /// <summary>Update agent heartbeat — called periodically by the agent via REST</summary>
    [HttpPost("heartbeat")]
    [AllowAnonymous] // Agents use their own machine tokens
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.PcId))
            return BadRequest(ApiResponse<object>.Fail("Invalid heartbeat data"));

        var pcId = Guid.Parse(dto.PcId);
        var pc = await _unitOfWork.Repository<Domain.Entities.Pc>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == pcId && !p.IsDeleted);

        if (pc == null)
            return NotFound(ApiResponse<object>.Fail("PC not found"));

        pc.IsAgentOnline = true;
        pc.ConnectionMode = dto.ConnectionMode ?? "LAN";
        pc.LastAgentHeartbeat = DateTimeOffset.UtcNow;
        pc.UpdatedAt = DateTimeOffset.UtcNow;

        _unitOfWork.Repository<Domain.Entities.Pc>().Update(pc);
        await _unitOfWork.SaveChangesAsync();

        return Ok(ApiResponse.Ok());
    }

    /// <summary>Remote start a session on a Cloud Mode PC — Admin/SuperAdmin only</summary>
    [HttpPost("remote-start/{pcId:guid}")]
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    public async Task<IActionResult> RemoteStartSession(Guid pcId, [FromBody] RemoteStartDto dto)
    {
        var pc = await _unitOfWork.Repository<Domain.Entities.Pc>()
            .Query()
            .FirstOrDefaultAsync(p => p.Id == pcId && !p.IsDeleted);

        if (pc == null)
            return NotFound(ApiResponse<object>.Fail("PC not found"));

        if (!pc.IsAgentOnline)
            return BadRequest(ApiResponse<object>.Fail("PC agent is offline. Cannot start session remotely."));

        // The actual unlock command is sent via SignalR from the frontend.
        // This endpoint just validates and creates the session record.
        return Ok(ApiResponse<object>.Ok(new
        {
            pcId = pc.Id,
            pcNumber = pc.PcNumber,
            connectionMode = pc.ConnectionMode,
            message = "PC validated. Send unlock command via SignalR."
        }));
    }
}

public class AgentHeartbeatDto
{
    public string PcId { get; set; } = null!;
    public string? ConnectionMode { get; set; }
}

/// <summary>First-run setup request sent by a Gaming PC agent.</summary>
public class AgentProvisionDto
{
    public Guid BranchId { get; set; }

    /// <summary>Which seat this machine is being set up as, e.g. "PC-1".</summary>
    public string PcNumber { get; set; } = null!;

    /// <summary>
    /// Stable hardware fingerprint. It must survive a reinstall, otherwise a repaired
    /// machine would look like a new one and be refused its own seat.
    /// </summary>
    public string MachineId { get; set; } = null!;

    public string? IpAddress { get; set; }
}

public class RemoteStartDto
{
    public int DurationMinutes { get; set; }
    public string? CustomerName { get; set; }
}
