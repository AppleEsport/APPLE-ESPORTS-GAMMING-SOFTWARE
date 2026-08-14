using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Where a super admin asks a branch to do something, and where the queue of what is still
/// owed to each branch can be seen.
///
/// The queuing half of the command system. See BranchCommand for why a command exists at all
/// rather than Head Office simply writing the branch's tables - a session stopped that way is
/// stopped only at Head Office, invisible to the counter that would need to bill it.
///
/// Head Office only. Delivery and confirmation happen inside the heartbeat itself, in
/// BranchHeartbeatController, because that connection already exists every three seconds and
/// a second one would only be a second thing to keep working.
/// </summary>
[ApiController]
[Route("api/branch-commands")]
[Authorize(Roles = Roles.SuperAdmin)]
public class BranchCommandsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<BranchCommandsController> _logger;

    public BranchCommandsController(AppDbContext db, ILogger<BranchCommandsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    /// <summary>
    /// Asks a branch to stop a session that is stuck open at Head Office's end.
    ///
    /// This is deliberately the one command type wired up today, for the one real need behind
    /// it: an operator gone home with a session still running, or a customer walked out
    /// without paying. It is not the only command this mechanism will ever carry - CommandType
    /// is a string precisely so PC maintenance, a forced logout, or whatever comes next can be
    /// added without touching how a command is delivered or confirmed.
    ///
    /// Queued, not executed. Whether this actually happens, and what it bills for, is decided
    /// entirely by the branch's own StopSessionAsync - the same path an operator's own click
    /// takes - when the command reaches it.
    /// </summary>
    [HttpPost("stop-session")]
    public async Task<IActionResult> StopSession([FromBody] StopSessionCommandDto dto, CancellationToken ct)
    {
        if (dto.BranchId == Guid.Empty || dto.SessionId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Which branch, and which session?", "MISSING_FIELDS"));

        if (!await _db.Branches.AnyAsync(b => b.Id == dto.BranchId, ct))
            return NotFound(ApiResponse<object>.Fail("No such branch.", "UNKNOWN_BRANCH"));

        // Refuses a session Head Office does not even believe is open, rather than queuing a
        // command that can only ever fail once it reaches the branch - the branch is still the
        // authority on whether it is really open, this just avoids sending it there for nothing.
        var session = await _db.Sessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.SessionId && s.BranchId == dto.BranchId, ct);

        if (session is null)
            return NotFound(ApiResponse<object>.Fail("Head Office has no such session for that branch.", "UNKNOWN_SESSION"));

        if (session.State != SessionState.Active)
            return BadRequest(ApiResponse<object>.Fail(
                $"That session is already {session.State}, not active.", "SESSION_NOT_ACTIVE"));

        // One at a time. A second stop queued for the same session while the first is still
        // travelling would either double-bill it or arrive to find nothing left to stop -
        // simpler to refuse the second and let the first resolve.
        var alreadyQueued = await _db.Set<BranchCommand>().AnyAsync(c =>
            c.BranchId == dto.BranchId
            && c.CommandType == "stop_session"
            && (c.Status == Domain.Entities.BranchCommandStatus.Pending || c.Status == Domain.Entities.BranchCommandStatus.Sent)
            && c.Payload.Contains(dto.SessionId.ToString()), ct);

        if (alreadyQueued)
            return BadRequest(ApiResponse<object>.Fail(
                "A stop for this session is already on its way to the branch.", "ALREADY_QUEUED"));

        var command = new BranchCommand
        {
            Id = Guid.NewGuid(),
            BranchId = dto.BranchId,
            CommandType = "stop_session",
            Payload = JsonSerializer.Serialize(new { sessionId = dto.SessionId, reason = dto.Reason }),
            Status = Domain.Entities.BranchCommandStatus.Pending,
            RequestedByUserId = CurrentUserId(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Add(command);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Queued a stop for session {SessionId} at branch {BranchId}, requested by {UserId}.",
            dto.SessionId, dto.BranchId, command.RequestedByUserId);

        return Ok(ApiResponse<object>.Ok(new
        {
            commandId = command.Id,
            status = "queued",
            message = "Sent. The branch picks this up within a few seconds and reports back once it is done.",
        }));
    }

    /// <summary>
    /// Every command for a branch, newest first - so a super admin can see whether a stop
    /// actually happened rather than assuming it did the moment the button was pressed.
    /// </summary>
    [HttpGet("branch/{branchId:guid}")]
    public async Task<IActionResult> ForBranch(Guid branchId, CancellationToken ct)
    {
        var commands = await _db.Set<BranchCommand>()
            .Where(c => c.BranchId == branchId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new
            {
                c.Id,
                c.CommandType,
                c.Payload,
                Status = c.Status.ToString(),
                c.CreatedAt,
                c.SentAt,
                c.CompletedAt,
                c.ResultMessage,
            })
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(commands));
    }
}

public class StopSessionCommandDto
{
    public Guid BranchId { get; set; }
    public Guid SessionId { get; set; }
    public string? Reason { get; set; }
}
