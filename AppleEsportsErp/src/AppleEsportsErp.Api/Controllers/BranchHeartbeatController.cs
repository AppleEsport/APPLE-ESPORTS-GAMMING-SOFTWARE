using System.Text;
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
    /// How long a branch may be silent before Head Office should say so.
    ///
    /// One minute, which at a three-second beat is twenty missed in a row - far past a dropped
    /// packet or a moment of bad signal, and still quick enough that a counter PC someone has
    /// switched off shows as gone while you are still standing there.
    ///
    /// This has to move whenever the beat does. Left at the two minutes that suited a
    /// thirty-second beat, it would have taken forty missed beats to admit a branch had
    /// stopped - a screen quietly showing a dead shop as healthy, which is the one thing this
    /// whole mechanism exists to prevent.
    /// </summary>
    public static readonly TimeSpan SilentAfter = TimeSpan.FromMinutes(1);

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

        // Two counter PCs both calling themselves one branch, caught the only moment it is
        // cheap to catch: while they are taking it in turns to overwrite each other.
        //
        // Checked before LastSeenAt moves, because "was the previous beat recent?" is the
        // whole test. Machines swapping every thirty seconds are two machines running at once;
        // a name that changed after an hour of silence is simply a replaced PC, which is a
        // normal thing to do and must not raise an alarm.
        if (!string.IsNullOrWhiteSpace(dto.MachineName)
            && !string.IsNullOrWhiteSpace(beat.ReportedByMachine)
            && !string.Equals(beat.ReportedByMachine, dto.MachineName, StringComparison.OrdinalIgnoreCase)
            && now - beat.LastSeenAt < SilentAfter)
        {
            beat.ConflictingMachine = beat.ReportedByMachine;
            beat.ConflictingMachineSeenAt = now;

            _logger.LogWarning(
                "Two PCs are both reporting as branch {BranchId}: {MachineA} and {MachineB}. " +
                "Each keeps its own records and syncs them under this one branch, so their " +
                "takings will be merged and cannot be separated afterwards. One of them should " +
                "be stopped.",
                dto.BranchId, beat.ReportedByMachine, dto.MachineName);
        }

        beat.ReportedByMachine = dto.MachineName;

        // Cleared once nothing has disagreed for a full silent window, so a clash that was
        // dealt with stops being reported and the warning keeps meaning something.
        if (beat.ConflictingMachineSeenAt is { } seen && now - seen > SilentAfter)
        {
            beat.ConflictingMachine = null;
            beat.ConflictingMachineSeenAt = null;
        }

        beat.LastSeenAt = now;

        // Converted here, and this one line is why no heartbeat ever reached Head Office.
        //
        // The branch sends its own wall clock, offset and all - 22:44 +05:30. PostgreSQL's
        // "timestamp with time zone" does not store an offset; it stores an instant, and
        // Npgsql refuses anything that is not already UTC rather than silently guessing.
        // So every beat threw on save and came back 500. Twenty a minute, from every branch,
        // for hours, and nothing on either screen said a word - Head Office looked like a
        // shop that had simply gone quiet.
        //
        // Nothing is lost. ToUniversalTime keeps the instant exactly, and the instant is the
        // whole point: comparing it against Head Office's own clock is how a branch with the
        // wrong time gets noticed. The +05:30 carried no information anyway - every branch
        // in this business is in India.
        //
        // Done on the receiving side deliberately. Head Office cannot assume every branch is
        // running today's build, and one shop with an odd clock must not be able to knock
        // over the endpoint that every other shop reports through.
        beat.BranchLocalTime = dto.BranchLocalTime.ToUniversalTime();

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

        // The reply carries this branch's settings back down, which is the half of sync that
        // never existed. Null when the branch already has them, so almost every beat stays a
        // few hundred bytes.
        var config = await ConfigForBranchIfChangedAsync(dto.BranchId, dto.ConfigVersion, ct);

        return Ok(ApiResponse<object>.Ok(new { received = true, at = now, config }));
    }

    /// <summary>
    /// This branch's operators and what they are allowed to see - but only if the branch does
    /// not already have exactly this.
    ///
    /// Scoped to the one branch, deliberately. Katargam's staff are no business of Adajan's
    /// counter PC, and sending the whole chain's operators to every shop would put every
    /// password hash in the company on four machines instead of one.
    /// </summary>
    private async Task<BranchConfigDto?> ConfigForBranchIfChangedAsync(
        Guid branchId, string? branchHasVersion, CancellationToken ct)
    {
        var operators = await _db.Operators.AsNoTracking()
            .Where(o => o.BranchId == branchId)
            .OrderBy(o => o.Id)   // stable order, or the fingerprint changes on its own
            .Select(o => new BranchOperatorConfigDto
            {
                Id = o.Id,
                FullName = o.FullName,
                Username = o.Username,
                Email = o.Email,
                PasswordHash = o.PasswordHash,
                MobileNumber = o.MobileNumber,
                AccessPin = o.AccessPin,
                IsGlobalAdmin = o.IsGlobalAdmin,
                DashboardPermissions = o.DashboardPermissions,
                IsBlocked = o.Status == OperatorStatus.Suspended || o.Status == OperatorStatus.Disabled,
            })
            .ToListAsync(ct);

        var config = new BranchConfigDto { Operators = operators };
        config.Version = Fingerprint(config);

        return string.Equals(config.Version, branchHasVersion, StringComparison.Ordinal)
            ? null                 // the branch is already correct; say nothing
            : config;
    }

    /// <summary>
    /// A short, stable fingerprint of the configuration.
    ///
    /// Computed from the content itself rather than kept as a column somebody has to remember
    /// to bump. A version number maintained by hand is a version number that eventually lies,
    /// and a branch that trusts a stale one would sit on old permissions for ever with both
    /// sides insisting they agree.
    /// </summary>
    private static string Fingerprint(BranchConfigDto config)
    {
        var canonical = string.Join('\n', config.Operators.Select(o => string.Join('',
            o.Id, o.FullName, o.Username, o.Email, o.PasswordHash,
            o.MobileNumber, o.AccessPin, o.IsGlobalAdmin, o.DashboardPermissions, o.IsBlocked)));

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
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

            // Nothing is written for a PC that has not moved, and this is what lets branches
            // report every three seconds instead of every thirty.
            //
            // The timestamps are the whole point. State and CurrentSessionId are compared by
            // EF, which skips an UPDATE when neither differs - but stamping LastActiveAt and
            // UpdatedAt unconditionally defeated that and forced a write for every PC on every
            // beat. Sixteen idle machines at four branches, twenty beats a minute, is around
            // 1,300 pointless row updates a minute for a chain where nothing is happening.
            //
            // It also made UpdatedAt useless. "Last changed" that changes every three seconds
            // whether or not anything changed answers no question anybody would ask of it.
            if (pc.State == state && pc.CurrentSessionId == reported.CurrentSessionId) continue;

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
                reportedByMachine = beat?.ReportedByMachine,

                // Surfaced beside the branch's own figures rather than buried in a log,
                // because every number on this row is wrong while two machines are supplying
                // them and nobody would think to doubt them otherwise.
                conflictingMachine = beat?.ConflictingMachine,
                conflictingMachineSeenAt = beat?.ConflictingMachineSeenAt,
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
