using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Application.DTOs;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/versions")]
[Authorize]
public class VersionController : ControllerBase
{
    private readonly IVersionService _versionService;

    public VersionController(IVersionService versionService)
    {
        _versionService = versionService;
    }

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
