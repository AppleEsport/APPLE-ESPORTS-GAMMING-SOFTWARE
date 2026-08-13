using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Configuration;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Where Head Office asks a branch to start or stop play, and watches whether it happened.
///
/// This is the "different mechanism" SessionService.RefuseIfHeadOffice pointed at: a command
/// sent down for the branch to carry out on its own database, through its own normal
/// StartSessionAsync/StopSessionAsync, so it shows up on the counter screen exactly as if the
/// operator had clicked it - never a session that exists only at Head Office.
///
/// Issuing only makes sense at Head Office, which is the only place with a screen watching
/// every branch at once. A branch issuing a command to itself would just be a slower way of
/// calling the session endpoints it already has.
/// </summary>
[ApiController]
[Route("api/branch-commands")]
[Authorize]
public class BranchCommandsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public BranchCommandsController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public class IssueCommandDto
    {
        public Guid BranchId { get; set; }
        public Guid? PcId { get; set; }
        public CommandType Type { get; set; }
        public JsonElement Payload { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Issue([FromBody] IssueCommandDto dto, CancellationToken ct)
    {
        if (!_configuration.IsHeadOffice())
            throw new AppException(
                "Remote commands can only be issued from Head Office - a branch already has direct " +
                "session endpoints and does not need to command itself.",
                System.Net.HttpStatusCode.BadRequest,
                "HEAD_OFFICE_ONLY_OPERATION");

        if (dto.BranchId == Guid.Empty || !await _db.Branches.AnyAsync(b => b.Id == dto.BranchId, ct))
            return NotFound(ApiResponse<object>.Fail("Head Office does not know that branch.", "UNKNOWN_BRANCH"));

        var command = new BranchCommand
        {
            Id = Guid.NewGuid(),
            BranchId = dto.BranchId,
            PcId = dto.PcId,
            Type = dto.Type,
            PayloadJson = dto.Payload.ValueKind == JsonValueKind.Undefined ? "{}" : dto.Payload.GetRawText(),
            Status = CommandStatus.Pending,
            IssuedByOperatorId = await this.GetOperatorIdAsync(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.BranchCommands.Add(command);
        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            id = command.Id,
            status = command.Status.ToString(),
        }));
    }

    /// <summary>
    /// Polled by the issuing screen while it shows "starting..." / "stopping..." and keeps the
    /// button locked. Stops meaning anything once the command reaches Confirmed or Failed -
    /// those never change again.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        var command = await _db.BranchCommands.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (command is null)
            return NotFound(ApiResponse<object>.Fail("No such command.", "NOT_FOUND"));

        return Ok(ApiResponse<object>.Ok(new
        {
            id = command.Id,
            status = command.Status.ToString(),
            resultMessage = command.ResultMessage,
            resultSessionId = command.ResultSessionId,
        }));
    }
}
