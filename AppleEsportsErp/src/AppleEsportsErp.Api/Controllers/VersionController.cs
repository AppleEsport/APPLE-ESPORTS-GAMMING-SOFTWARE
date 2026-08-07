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
