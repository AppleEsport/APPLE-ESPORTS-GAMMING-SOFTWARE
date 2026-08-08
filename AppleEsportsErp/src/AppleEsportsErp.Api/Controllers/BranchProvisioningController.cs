using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
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
    public async Task<IActionResult> GetBranchProvisioning(Guid branchId)
    {
        var branch = await _db.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId);
        if (branch is null)
            return NotFound(ApiResponse<object>.Fail("Branch not found at Head Office.", "BRANCH_NOT_FOUND"));

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
}
