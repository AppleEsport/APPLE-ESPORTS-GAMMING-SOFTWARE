using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Infrastructure.Services;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Records what an operator says happened while their shift was unattended.
///
/// A shift only closes properly through the end-shift flow, with the cash counted. So an
/// operator who lost power, or whose PC was switched off with the app running, comes back to
/// a shift still open with a hole in the middle of it. The system can see the hole. It cannot
/// tell whether the shop was shut for the night or the power went out, and guessing either
/// way is wrong: assume a fault and the owner gets a power-cut email most mornings; assume a
/// normal close and a real overnight outage is never reported.
///
/// So it asks the one person who knows. Nothing is emailed to the owner until they answer.
/// </summary>
[ApiController]
[Route("api/shift-gap")]
[Authorize]
public class ShiftGapController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAdminNotifier _notifier;
    private readonly IAuditService _audit;
    private readonly ILogger<ShiftGapController> _logger;

    public ShiftGapController(
        AppDbContext db,
        IAdminNotifier notifier,
        IAuditService audit,
        ILogger<ShiftGapController> logger)
    {
        _db = db;
        _notifier = notifier;
        _audit = audit;
        _logger = logger;
    }

    [HttpPost("explain")]
    public async Task<IActionResult> Explain([FromBody] ExplainGapDto dto, CancellationToken ct)
    {
        if (dto is null || dto.ShiftId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Which shift?", "SHIFT_REQUIRED"));

        var operatorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var oid) ? oid : Guid.Empty;

        var shift = await _db.Shifts
            .FirstOrDefaultAsync(s => s.Id == dto.ShiftId && s.OperatorId == operatorId, ct);

        if (shift is null)
            return NotFound(ApiResponse<object>.Fail("That shift is not yours, or does not exist.", "SHIFT_NOT_FOUND"));

        var branchName = await _db.Branches.Where(b => b.Id == shift.BranchId)
            .Select(b => b.Name).FirstOrDefaultAsync(ct) ?? "Unknown branch";

        var operatorName = await _db.Operators.Where(o => o.Id == operatorId)
            .Select(o => o.FullName).FirstOrDefaultAsync(ct) ?? "Unknown operator";

        var gapStart = DateTimeOffset.UtcNow.AddMinutes(-Math.Max(1, dto.UnattendedMinutes));
        var gapEnd = DateTimeOffset.UtcNow;
        var howLong = AdminEmailTemplate.Describe(TimeSpan.FromMinutes(Math.Max(1, dto.UnattendedMinutes)));

        // "The shop was simply shut" is the common answer and is not an incident. It is still
        // written to the audit trail, so nobody can later claim an outage went unreported.
        if (dto.Reason == GapReason.ShopWasClosed)
        {
            await _audit.LogAsync(new AuditEntry
            {
                OperatorId = operatorId,
                UserRole = "Operator",
                UserName = operatorName,
                Action = "shift_gap_explained_normal",
                BranchId = shift.BranchId,
                BranchName = branchName,
                TargetType = "shift",
                TargetId = shift.Id,
                Details = new { unattendedMinutes = dto.UnattendedMinutes, reason = dto.Reason.ToString(), note = dto.Note },
            });

            _logger.LogInformation(
                "Operator {Operator} says the {Gap} gap on shift {ShiftId} was the shop being closed. Nothing reported.",
                operatorName, howLong, shift.Id);

            return Ok(ApiResponse<object>.Ok(new { recorded = true, reported = false }));
        }

        // A real interruption. Recorded against the day it started, so an overnight cut lands
        // on the night it disrupted rather than the morning it was noticed.
        var kind = dto.Reason == GapReason.InternetWentDown
            ? DowntimeKind.InternetOffline
            : DowntimeKind.PowerOrRestart;

        _db.DowntimeEvents.Add(new DowntimeEvent
        {
            Id = Guid.NewGuid(),
            BranchId = shift.BranchId,
            Kind = kind,
            StartedAt = gapStart,
            EndedAt = gapEnd,
            DurationSeconds = Math.Max(60, dto.UnattendedMinutes * 60),
            SessionsAffected = 0,
            BusinessDay = IndiaTime.BusinessDayOf(gapStart),
            Notes = string.IsNullOrWhiteSpace(dto.Note)
                ? $"Reported by {operatorName} when they logged back in."
                : $"Reported by {operatorName}: {dto.Note}",
            CreatedAt = gapEnd,
        });

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(new AuditEntry
        {
            OperatorId = operatorId,
            UserRole = "Operator",
            UserName = operatorName,
            Action = "shift_gap_explained_incident",
            BranchId = shift.BranchId,
            BranchName = branchName,
            TargetType = "shift",
            TargetId = shift.Id,
            Details = new { unattendedMinutes = dto.UnattendedMinutes, reason = dto.Reason.ToString(), note = dto.Note },
        });

        var what = dto.Reason == GapReason.InternetWentDown ? "lost its internet" : "lost power";

        await _notifier.NotifyAsync(
            $"{branchName} {what} for about {howLong}",
            AdminEmailTemplate.Compose(
                $"{branchName} {what}",
                dto.Reason == GapReason.InternetWentDown ? AdminEmailTemplate.Amber : AdminEmailTemplate.Red,
                $"{operatorName} has just logged back in at {branchName} and says the shop {what} " +
                $"for about {howLong}. This is their own account of it, not something the system worked out.",
                new[]
                {
                    ("Branch", branchName),
                    ("Reported by", operatorName),
                    ("", ""),
                    ("What happened", dto.Reason == GapReason.InternetWentDown ? "The internet went down" : "The power went off"),
                    ("Roughly from", IndiaTime.Format(gapStart)),
                    ("Back at", IndiaTime.Format(gapEnd)),
                    ("For about", howLong),
                    ("", ""),
                    ("Their note", string.IsNullOrWhiteSpace(dto.Note) ? "-" : dto.Note!),
                    ("Counts towards", $"{IndiaTime.BusinessDayOf(gapStart):dd MMM yyyy}"),
                },
                headline: $"Down about {howLong}",
                footnote: "The times are approximate, taken from when the system was last used and " +
                          "when the operator logged back in. It will appear on that day's report."),
            ct);

        _logger.LogInformation(
            "Operator {Operator} reported a {Kind} of {Gap} at {Branch}.", operatorName, kind, howLong, branchName);

        return Ok(ApiResponse<object>.Ok(new { recorded = true, reported = true }));
    }

    public enum GapReason
    {
        /// <summary>Nothing was wrong - the shop was shut and nobody was using the system.</summary>
        ShopWasClosed = 0,

        /// <summary>The power went off, so the whole system stopped.</summary>
        PowerWentOff = 1,

        /// <summary>The internet went down. The shop kept working; only reporting stopped.</summary>
        InternetWentDown = 2,
    }

    public class ExplainGapDto
    {
        public Guid ShiftId { get; set; }
        public int UnattendedMinutes { get; set; }
        public GapReason Reason { get; set; }

        /// <summary>Anything the operator wants to add, in their own words. Optional.</summary>
        public string? Note { get; set; }
    }
}
