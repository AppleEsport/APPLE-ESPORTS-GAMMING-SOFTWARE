using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Api.Services;
using AppleEsportsErp.Application.DTOs;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/versions")]
[Authorize]
public class VersionController : ControllerBase
{
    private readonly IVersionService _versionService;
    private readonly AppDbContext _db;
    private readonly IRemoteBranchControl _remote;

    public VersionController(IVersionService versionService, AppDbContext db, IRemoteBranchControl remote)
    {
        _versionService = versionService;
        _db = db;
        _remote = remote;
    }

    private Guid CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    [HttpGet("latest")]
    [AllowAnonymous]
    public async Task<IActionResult> GetLatestVersion()
    {
        var version = await _versionService.GetLatestVersionAsync();
        if (version == null)
            return Ok(ApiResponse<object>.Ok(null));

        return Ok(ApiResponse<VersionInfoDto>.Ok(version));
    }

    /// <summary>
    /// Readable by any signed-in operator or admin, not just the owner. The whole point of
    /// keeping the history is that the person about to apply an update can see what is in it
    /// first, and that person is the operator.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetVersionHistory()
    {
        var versions = await _versionService.GetVersionHistoryAsync();
        return Ok(ApiResponse<List<VersionInfoDto>>.Ok(versions));
    }

    [HttpGet("branch/{branchId}")]
    public async Task<IActionResult> GetBranchVersionStatus(Guid branchId)
    {
        var status = await _versionService.GetBranchVersionStatusAsync(branchId);
        if (status == null)
            return Ok(ApiResponse<object>.Ok(null));

        return Ok(ApiResponse<BranchVersionStatusDto>.Ok(status));
    }

    [HttpGet("all-branches")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> GetAllBranchVersionStatuses()
    {
        var statuses = await _versionService.GetAllBranchVersionStatusesAsync();
        return Ok(ApiResponse<List<BranchVersionStatusDto>>.Ok(statuses));
    }

    [HttpPost("create")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> CreateVersion([FromBody] CreateVersionDto dto)
    {
        var version = await _versionService.CreateVersionAsync(dto.Version, dto.ReleaseNotes);
        return Ok(ApiResponse<VersionInfoDto>.Ok(version));
    }

    [HttpPost("approve")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> ApproveVersion([FromBody] ApproveUpdateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var version = await _versionService.ApproveVersionAsync(dto.VersionInfoId, userId);
        return Ok(ApiResponse<VersionInfoDto>.Ok(version));
    }

    [HttpPut("branch/{branchId}/auto-update")]
    [Authorize]
    public async Task<IActionResult> UpdateBranchAutoUpdate(Guid branchId, [FromBody] UpdateBranchAutoUpdateDto dto)
    {
        await _versionService.UpdateBranchAutoUpdateAsync(branchId, dto.AutoUpdateEnabled);
        return Ok(ApiResponse<object>.Ok("Auto-update setting updated"));
    }

    [HttpPost("branch/{branchId}/status")]
    [AllowAnonymous]
    public async Task<IActionResult> UpdateBranchVersionStatus(Guid branchId, [FromBody] UpdateBranchVersionStatusDto dto)
    {
        await _versionService.UpdateBranchVersionStatusAsync(branchId, dto.CurrentVersion, dto.UpToDateCount, dto.TotalCount);
        return Ok(ApiResponse<object>.Ok("Version status updated"));
    }

    /// <summary>
    /// Where a branch says how its update is going, so the Updates page can show a progress bar
    /// that reflects something real.
    ///
    /// Anonymous for the same reason the status endpoint above is: a branch mid-update may be
    /// restarting its own services and cannot be relied on to hold a session through it. It
    /// writes only progress text against a branch id — there is nothing here worth forging, and
    /// the version a branch is allowed to install is still governed by approval.
    ///
    /// Nothing calls this yet. The branch app that will is Phase 2.
    /// </summary>
    [HttpPost("branch/{branchId}/progress")]
    [AllowAnonymous]
    public async Task<IActionResult> ReportUpdateProgress(Guid branchId, [FromBody] UpdateProgressDto dto)
    {
        await _versionService.ReportUpdateProgressAsync(branchId, dto.Stage, dto.ProgressPercent, dto.Message);
        return Ok(ApiResponse<object>.Ok("Progress recorded"));
    }

    /// <summary>
    /// Sends one branch an exact, already-published version to run - the one remote action
    /// that can go backwards. Every other update path in this system only ever moves a branch
    /// forward, because a branch checking on its own schedule (desktop-client's UpdateService)
    /// deliberately refuses anything that is not strictly newer than what it already runs.
    /// This is different: a person at Head Office has decided precisely what a branch should
    /// be running, older or newer, and that decision is allowed to override the branch's own
    /// caution.
    ///
    /// Restricted to Super Admin alone, tighter than every other remote command in this
    /// system. If the branch's own attempt to carry this out fails partway, it can take down
    /// the very service that would have reported the failure - there is no channel left for
    /// Head Office to retry through, only physical or remote-desktop access to that PC. See
    /// BranchHeartbeatService.RunInstallVersionAsync.
    /// </summary>
    [HttpPost("branch/{branchId}/install")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> InstallVersionOnBranch(
        Guid branchId, [FromBody] InstallVersionDto dto, CancellationToken ct)
    {
        if (!_remote.MustTravel)
            return BadRequest(ApiResponse<object>.Fail(
                "This only makes sense from Head Office, telling a branch what to run.",
                "HEAD_OFFICE_ONLY"));

        if (string.IsNullOrWhiteSpace(dto.Version))
            return BadRequest(ApiResponse<object>.Fail("Which version should this branch run?", "VERSION_REQUIRED"));

        var version = await _db.Set<AppleEsportsErp.Domain.Entities.VersionInfo>().AsNoTracking()
            .FirstOrDefaultAsync(v => v.CurrentVersion == dto.Version, ct);

        if (version is null)
            return NotFound(ApiResponse<object>.Fail(
                $"There is no version {dto.Version} on record.", "VERSION_NOT_FOUND"));

        if (string.IsNullOrWhiteSpace(version.InstallerFileName) || string.IsNullOrWhiteSpace(version.InstallerSha256))
            return BadRequest(ApiResponse<object>.Fail(
                $"Version {dto.Version} has no installer published against it, so there is nothing to send.",
                "NO_INSTALLER"));

        var receipt = await _remote.SendAsync(branchId, BranchCommands.InstallVersion, new
        {
            version = version.CurrentVersion,
            sha256 = version.InstallerSha256,
            downloadPath = $"/api/releases/download/{version.InstallerFileName}",
            sizeBytes = version.InstallerSizeBytes,
            reason = dto.Reason,
        }, CurrentUserId(), ct);

        return Accepted(ApiResponse<object>.Ok(new
        {
            queued = true,
            commandId = receipt.CommandId,
            branchIsReporting = receipt.BranchIsReporting,
            message = receipt.Message,
        }));
    }
}

public class UpdateProgressDto
{
    /// <summary>"downloading", "installing", "restarting", "done" or "failed".</summary>
    public string Stage { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public string? Message { get; set; }
}

public class CreateVersionDto
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
}

public class UpdateBranchVersionStatusDto
{
    public string CurrentVersion { get; set; } = string.Empty;
    public int UpToDateCount { get; set; }
    public int TotalCount { get; set; }
}

public class InstallVersionDto
{
    public string Version { get; set; } = string.Empty;

    /// <summary>Why, for whoever reads the audit trail later - "downgrading after 2.4.9 was rolled back", not left blank.</summary>
    public string? Reason { get; set; }
}
