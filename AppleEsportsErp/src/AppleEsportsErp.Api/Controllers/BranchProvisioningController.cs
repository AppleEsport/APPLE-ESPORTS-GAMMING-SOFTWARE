using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Services;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Lets a fresh branch install pull its identity down from Head Office.
///
/// This is what makes sync actually usable. Both sides seed their own data with freshly
/// generated GUIDs, so a branch that sets itself up independently ends up with a different
/// id for the same physical PC — and every session it reports is then rejected at Head
/// Office with "no PC with that id". Pulling the real identifiers down at setup, instead of
/// inventing local ones, is what makes the two databases talk about the same things.
///
/// Read-only and deliberately anonymous: a branch has no credentials until it has been set
/// up, which is the very thing this call enables. It exposes only what a branch needs to
/// identify itself — no customers, no takings, no password hashes.
/// </summary>
[ApiController]
[Route("api/provisioning")]
public class BranchProvisioningController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<BranchProvisioningController> _logger;

    public BranchProvisioningController(AppDbContext db, ILogger<BranchProvisioningController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Branches an installer can offer to set this machine up as.</summary>
    [HttpGet("branches")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBranches()
    {
        var branches = await _db.Branches
            .AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new
            {
                b.Id,
                b.Name,
                b.Address,
                openingTime = b.OpeningTime.ToString("HH:mm"),
                closingTime = b.ClosingTime.ToString("HH:mm"),
                status = b.Status.ToString(),
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(branches));
    }

    /// <summary>
    /// The identity a branch needs so its records line up with Head Office: the branch's own
    /// id, its PCs and its operators, all with Head Office's identifiers rather than
    /// locally invented ones.
    /// </summary>
    [HttpGet("branch/{branchId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBranchProvisioning(
        Guid branchId, [FromQuery] string? machineName, [FromQuery] bool force = false)
    {
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch is null)
            return NotFound(ApiResponse<object>.Fail("Branch not found at Head Office.", "BRANCH_NOT_FOUND"));

        // A second machine adopting a branch identity that is already live is exactly how two
        // tills end up recording one shop's takings separately, with no way to un-merge them
        // afterwards - see BranchHeartbeatController.Receive's own warning for what that costs
        // once it happens. Caught here rather than there, because this is the one place that
        // can see every branch's heartbeat before the damage starts: a fresh install's own
        // database is empty and has no way to know somebody else is already running this
        // branch, so it cannot refuse on its own.
        if (!force && !string.IsNullOrWhiteSpace(machineName))
        {
            var beat = await _db.Set<BranchHeartbeat>().AsNoTracking()
                .FirstOrDefaultAsync(h => h.BranchId == branchId);

            if (beat is not null
                && !string.IsNullOrWhiteSpace(beat.ReportedByMachine)
                && !string.Equals(beat.ReportedByMachine, machineName, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow - beat.LastSeenAt < BranchHeartbeatController.SilentAfter)
            {
                return Conflict(ApiResponse<object>.Fail(
                    $"{branch.Name} is already running on {beat.ReportedByMachine}, and it reported in " +
                    "within the last minute - it is not switched off. Setting this PC up as the same " +
                    "branch will start two tills recording this shop's takings separately, with no way " +
                    "to un-merge them afterwards. If you are deliberately replacing the PC that runs " +
                    "this branch, stop the Apple Esports API service on the old PC first - then confirm " +
                    "you want to continue anyway.",
                    "BRANCH_ALREADY_ACTIVE"));
            }
        }

        var pcs = await _db.Pcs
            .AsNoTracking()
            .Where(p => p.BranchId == branchId && !p.IsDeleted)
            .OrderBy(p => p.PcNumber)
            .Select(p => new
            {
                p.Id,
                p.PcNumber,
                p.PcName,
                p.Zone,
                p.MonitorHz,
                p.PricingProfileId,
                // So the installer can warn before a second machine tries to take a seat
                // that is already claimed, rather than failing at the end of setup.
                isProvisioned = p.MachineId != null,
                state = p.State.ToString(),
            })
            .ToListAsync();

        var operators = await _db.Operators
            .AsNoTracking()
            .Where(o => o.BranchId == branchId)
            .OrderBy(o => o.FullName)
            .Select(o => new
            {
                o.Id,
                o.FullName,
                o.Username,
                status = o.Status.ToString(),
                // Never the password hash. This endpoint is unauthenticated by necessity,
                // so it must not hand out anything usable for impersonation.
            })
            .ToListAsync();

        var pricingProfiles = await _db.PricingProfiles
            .AsNoTracking()
            .Where(p => p.BranchId == branchId)
            .Select(p => new { p.Id, p.Name, p.BaseHourlyRate, p.BufferMinutes })
            .ToListAsync();

        _logger.LogInformation(
            "Provisioning data served for branch {BranchName}: {Pcs} PCs, {Operators} operators.",
            branch.Name, pcs.Count, operators.Count);

        return Ok(ApiResponse<object>.Ok(new
        {
            branch = new
            {
                branch.Id,
                branch.Name,
                branch.Address,
                openingTime = branch.OpeningTime.ToString("HH:mm"),
                closingTime = branch.ClosingTime.ToString("HH:mm"),
            },
            pcs,
            operators,
            pricingProfiles,
            servedAt = DateTimeOffset.UtcNow,
        }));
    }

    /// <summary>
    /// Cheap reachability probe for the installer, so someone typing the server address
    /// finds out immediately whether it is right rather than after finishing setup.
    /// </summary>
    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping() => Ok(ApiResponse<object>.Ok(new
    {
        service = "Apple Esports Head Office",
        time = DateTimeOffset.UtcNow,
    }));

    // ── The branch side ───────────────────────────────────────────────────────
    //
    // Everything above is answered BY Head Office. Everything below runs ON a branch and
    // calls Head Office. Same code either way — a branch and Head Office run the same
    // build, and which role a machine plays is a matter of configuration, not of binary.

    /// <summary>
    /// Whether this branch is actually reaching Head Office, and what is still waiting.
    ///
    /// Exists because there was no way to answer "is my branch reporting?" short of reading
    /// the service log — and a branch whose sync is broken looks exactly like one whose sync
    /// is fine, from behind the counter. Anything queued and not delivered is money and
    /// sessions Head Office cannot see.
    /// </summary>
    [HttpGet("sync-status")]
    [AllowAnonymous]
    public async Task<IActionResult> SyncStatus(
        [FromServices] BranchAdoptionService adoption, CancellationToken ct)
    {
        var pending = await _db.SyncOutboxEntries.CountAsync(e => e.SyncedAt == null, ct);
        var delivered = await _db.SyncOutboxEntries.CountAsync(e => e.SyncedAt != null, ct);

        var lastDelivered = await _db.SyncOutboxEntries
            .Where(e => e.SyncedAt != null)
            .OrderByDescending(e => e.SyncedAt)
            .Select(e => e.SyncedAt)
            .FirstOrDefaultAsync(ct);

        // The oldest thing still waiting says how far behind Head Office is, which matters
        // more than how many are queued: one entry stuck since yesterday is worse news than
        // fifty from the last minute.
        var oldestPending = await _db.SyncOutboxEntries
            .Where(e => e.SyncedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Select(e => (DateTimeOffset?)e.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var lastError = await _db.SyncOutboxEntries
            .Where(e => e.SyncedAt == null && e.LastError != null)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => e.LastError)
            .FirstOrDefaultAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            headOfficeUrl = adoption.HeadOfficeUrl,
            configured = !string.IsNullOrWhiteSpace(adoption.HeadOfficeUrl),
            pending,
            delivered,
            lastDeliveredAt = lastDelivered,
            oldestPendingAt = oldestPending,
            lastError,
        }));
    }

    /// <summary>What this machine currently believes itself to be.</summary>
    [HttpGet("identity")]
    [AllowAnonymous]
    public async Task<IActionResult> Identity(
        [FromServices] BranchAdoptionService adoption, CancellationToken ct)
        => Ok(ApiResponse<object>.Ok(await adoption.DescribeIdentityAsync(ct)));

    /// <summary>
    /// Head Office's branch list, fetched by this machine on the caller's behalf.
    ///
    /// Proxied rather than called from the dashboard directly so that Head Office's address
    /// stays in one place — the branch's own configuration — instead of being duplicated
    /// into every client that needs it and drifting when the owner's server arrives.
    /// </summary>
    [HttpGet("head-office/branches")]
    [AllowAnonymous]
    public async Task<IActionResult> HeadOfficeBranches(
        [FromServices] BranchAdoptionService adoption, CancellationToken ct)
    {
        var (ok, error, payload) = await adoption.ListHeadOfficeBranchesAsync(ct);
        if (!ok) return StatusCode(502, ApiResponse<object>.Fail(error!, "HEAD_OFFICE_UNREACHABLE"));

        return Content(payload!, "application/json");
    }

    /// <summary>
    /// Sets this machine up as a particular branch, taking Head Office's identifiers for the
    /// branch, its PCs, its pricing and its operators.
    ///
    /// Anonymous of necessity: a branch has no credentials until it has been set up, and this
    /// is the call that sets it up. It is not a way in — it only replaces local identifiers
    /// with Head Office's, refuses outright once the machine has taken money, and grants no
    /// access to anything.
    /// </summary>
    [HttpPost("adopt")]
    [AllowAnonymous]
    public async Task<IActionResult> Adopt(
        [FromBody] AdoptRequest request,
        [FromServices] BranchAdoptionService adoption,
        CancellationToken ct)
    {
        if (request is null || request.BranchId == Guid.Empty)
            return BadRequest(ApiResponse<object>.Fail("Choose which branch this PC is.", "BRANCH_REQUIRED"));

        var result = await adoption.AdoptAsync(request.BranchId, request.Force, ct);

        if (!result.Success)
            return BadRequest(ApiResponse<object>.Fail(result.Error!, "ADOPTION_FAILED"));

        return Ok(ApiResponse<object>.Ok(new
        {
            branchId = result.BranchId,
            branchName = result.BranchName,
            pcs = result.Pcs,
            operators = result.Operators,
            pricingProfiles = result.PricingProfiles,
            operatorsNeedingPassword = result.OperatorsNeedingPassword,
        }));
    }

    public sealed class AdoptRequest
    {
        public Guid BranchId { get; set; }

        /// <summary>
        /// Re-adopt a machine that has already traded. Deliberately not offered in the
        /// wizard: it orphans existing sessions and takings, so it should take a deliberate
        /// act by someone who knows that.
        /// </summary>
        public bool Force { get; set; }
    }
}
