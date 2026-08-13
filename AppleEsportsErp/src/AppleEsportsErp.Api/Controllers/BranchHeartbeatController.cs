using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Sync;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Where a branch says what it is doing right now, and where Head Office reads it back.
///
/// The counterpart to the sync inbox, and deliberately nothing like it. The inbox carries
/// history and may never lose a record; this carries state, where only the newest matters and
/// a missed beat costs nothing. Trying to serve both with one mechanism is why Head Office
/// could show a branch's bills from three weeks ago and not know whether anybody was standing
/// at the counter.
/// </summary>
[ApiController]
[Route("api/branch-status")]
public class BranchHeartbeatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<BranchHeartbeatController> _logger;

    /// <summary>
    /// How long a branch may be silent before Head Office should say so. Four missed beats:
    /// long enough that one dropped connection is not an alarm, short enough that a shop which
    /// has genuinely stopped reporting is noticed within a couple of minutes.
    /// </summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromMinutes(2);

    public BranchHeartbeatController(AppDbContext db, ILogger<BranchHeartbeatController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a branch's picture of itself.
    ///
    /// Anonymous, like the rest of the branch-to-Head-Office endpoints: a counter PC has no
    /// person signed in at 4am and must still report. Nothing here can be used to read data
    /// or move money — it only overwrites one branch's own live figures with what that branch
    /// says about itself, which it is the authority on.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Receive([FromBody] BranchHeartbeatDto dto, CancellationToken ct)
    {
        if (dto is null || dto.BranchId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Which branch?", "BRANCH_REQUIRED"));

        if (!await _db.Branches.AnyAsync(b => b.Id == dto.BranchId, ct))
            return NotFound(ApiResponse<object>.Fail("Head Office does not know that branch.", "UNKNOWN_BRANCH"));

        var now = DateTimeOffset.UtcNow;

        var beat = await _db.Set<BranchHeartbeat>().FirstOrDefaultAsync(h => h.BranchId == dto.BranchId, ct);
        if (beat is null)
        {
            beat = new BranchHeartbeat { BranchId = dto.BranchId };
            _db.Add(beat);
        }

        beat.LastSeenAt = now;
        beat.BranchLocalTime = dto.BranchLocalTime;
        beat.Version = dto.Version;
        beat.OperatorsOnDuty = JsonSerializer.Serialize(dto.OperatorsOnDuty);
        beat.OperatorsOnDutyCount = dto.OperatorsOnDuty.Count;
        beat.ActiveSessions = dto.ActiveSessions;
        beat.PcsTotal = dto.Pcs.Count;
        beat.PcsBusy = dto.Pcs.Count(p => !string.Equals(p.State, "idle", StringComparison.OrdinalIgnoreCase));
        beat.DrawerExpected = dto.DrawerExpected;
        beat.TakingsToday = dto.TakingsToday;
        beat.UndeliveredRecords = dto.UndeliveredRecords;

        await ApplyOperatorsOnDutyAsync(dto, ct);
        await ApplyPcStatesAsync(dto, ct);

        await _db.SaveChangesAsync(ct);

        return Ok(ApiResponse<object>.Ok(new { received = true, at = now }));
    }

    /// <summary>
    /// Marks the branch's operators on or off duty to match what the branch says.
    ///
    /// This is the fix for a super admin opening Settings and seeing "logged out" beside an
    /// operator who is at that moment running the shop. Nothing ever told Head Office
    /// otherwise — an operator's status was written only where they logged in, which is the
    /// branch, and it stayed there.
    ///
    /// Scoped strictly to this branch's own operators. A heartbeat from Adajan must never be
    /// able to sign somebody out at Katargam.
    /// </summary>
    private async Task ApplyOperatorsOnDutyAsync(BranchHeartbeatDto dto, CancellationToken ct)
    {
        var onDuty = dto.OperatorsOnDuty.Select(o => o.OperatorId).ToHashSet();

        var branchOperators = await _db.Operators
            .Where(o => o.BranchId == dto.BranchId)
            .ToListAsync(ct);

        foreach (var op in branchOperators)
        {
            var isOn = onDuty.Contains(op.Id);

            // Suspended and disabled accounts are left exactly as they are. Those are Head
            // Office's decisions about a person, not a branch's observation of a shift, and a
            // heartbeat must not quietly reinstate somebody an admin has just suspended.
            if (op.Status is OperatorStatus.Suspended or OperatorStatus.Disabled) continue;

            op.Status = isOn ? OperatorStatus.Active : OperatorStatus.LoggedOut;
            op.IsOnline = isOn;
        }
    }

    /// <summary>
    /// Makes Head Office's PC grid show what the counter's grid shows.
    ///
    /// Session events already move a PC between busy and free, but only for the PCs a session
    /// happens on. Everything else — a machine put into maintenance, one held for a
    /// reservation, one waiting for the customer to pay — never moved at all, which is how
    /// three of Adajan's PCs sat on "awaiting billing" from early August against sessions that
    /// no longer existed.
    /// </summary>
    private async Task ApplyPcStatesAsync(BranchHeartbeatDto dto, CancellationToken ct)
    {
        if (dto.Pcs.Count == 0) return;

        var ids = dto.Pcs.Select(p => p.PcId).ToList();
        var pcs = await _db.Pcs
            .Where(p => p.BranchId == dto.BranchId && ids.Contains(p.Id))
            .ToListAsync(ct);

        foreach (var pc in pcs)
        {
            var reported = dto.Pcs.First(p => p.PcId == pc.Id);

            if (!Enum.TryParse<PcState>(reported.State.Replace("_", ""), ignoreCase: true, out var state))
                continue;   // a state this Head Office build does not know; leave it alone

            pc.State = state;
            pc.CurrentSessionId = reported.CurrentSessionId;
            pc.LastActiveAt = DateTimeOffset.UtcNow;
            pc.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Every branch and whether it is still talking, for the super admin's overview.
    ///
    /// A branch that has never reported appears too, rather than being left out. "Never heard
    /// from" is the single most important thing this screen can say, and omitting the row
    /// would hide exactly the branch worth worrying about.
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var branches = await _db.Branches
            .Where(b => b.Status == BranchStatus.Active)
            .OrderBy(b => b.Name)
            .Select(b => new { b.Id, b.Name })
            .ToListAsync(ct);

        var beats = await _db.Set<BranchHeartbeat>().ToDictionaryAsync(h => h.BranchId, ct);

        var rows = branches.Select(b =>
        {
            beats.TryGetValue(b.Id, out var beat);
            var silent = beat is null || now - beat.LastSeenAt > SilentAfter;

            return new
            {
                branchId = b.Id,
                branchName = b.Name,
                reporting = !silent,
                lastSeenAt = beat?.LastSeenAt,
                secondsSinceLastSeen = beat is null ? (int?)null : (int)(now - beat.LastSeenAt).TotalSeconds,
                version = beat?.Version,
                operatorsOnDuty = beat is null
                    ? new List<OperatorOnDutyDto>()
                    : JsonSerializer.Deserialize<List<OperatorOnDutyDto>>(beat.OperatorsOnDuty ?? "[]")
                      ?? new List<OperatorOnDutyDto>(),
                activeSessions = beat?.ActiveSessions ?? 0,
                pcsBusy = beat?.PcsBusy ?? 0,
                pcsTotal = beat?.PcsTotal ?? 0,
                drawerExpected = beat?.DrawerExpected,
                takingsToday = beat?.TakingsToday ?? 0m,
                undeliveredRecords = beat?.UndeliveredRecords ?? 0,
            };
        });

        return Ok(ApiResponse<object>.Ok(rows));
    }
}
