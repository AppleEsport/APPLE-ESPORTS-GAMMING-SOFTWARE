using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Where a fix actually travels from Head Office to the branches.
///
/// A Super Admin uploads an installer here, approves it on the Updates page, and every branch
/// picks it up on its next check. That is the whole point of the version machinery: without
/// somewhere to fetch the file from, approving a version only ever changed a number on a
/// screen.
/// </summary>
[ApiController]
[Route("api/releases")]
public class ReleasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ReleasesController> _logger;

    // Installers are large and rarely change; 200 MB leaves room for one that bundles
    // PostgreSQL without inviting someone to POST a DVD image.
    private const long MaxInstallerBytes = 200L * 1024 * 1024;

    public ReleasesController(AppDbContext db, IWebHostEnvironment env, ILogger<ReleasesController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    private string ReleaseDirectory
    {
        get
        {
            // Outside wwwroot on purpose. Anything under wwwroot is served as a static file
            // by the dashboard host, which would publish every installer — including ones
            // not yet approved — to anyone who guessed the name.
            var dir = Path.Combine(_env.ContentRootPath, "releases");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Uploads the installer for a version and records its hash. Super Admin only: whatever
    /// is accepted here is what every branch will download and execute.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = Roles.SuperAdmin)]
    [RequestSizeLimit(MaxInstallerBytes)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string version, [FromForm] string? releaseNotes)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No installer was uploaded."));

        if (string.IsNullOrWhiteSpace(version))
            return BadRequest(ApiResponse<object>.Fail("A version number is required."));

        if (!file.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("The installer must be a .exe file."));

        // Build the stored name ourselves from the version. Trusting the uploaded file name
        // would let "..\..\appsettings.json" out of the releases folder.
        var safeVersion = new string(version.Trim().Where(c => char.IsLetterOrDigit(c) || c is '.' or '-').ToArray());
        if (safeVersion.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("That version number is not usable as a file name."));

        var fileName = $"AppleEsports-Setup-{safeVersion}.exe";
        var path = Path.Combine(ReleaseDirectory, fileName);

        await using (var target = System.IO.File.Create(path))
        {
            await file.CopyToAsync(target);
        }

        // Hash what actually landed on disk, not what was in memory — this is the value
        // branches will check the download against.
        string hash;
        await using (var stream = System.IO.File.OpenRead(path))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
        }

        var existing = await _db.VersionInfos.FirstOrDefaultAsync(v => v.CurrentVersion == safeVersion);
        if (existing is null)
        {
            existing = new VersionInfo
            {
                CurrentVersion = safeVersion,
                CreatedAt = DateTime.UtcNow,
                ApprovedForRollout = false,   // still needs a Super Admin to approve it
                ReleaseNotes = releaseNotes ?? "",
            };
            _db.VersionInfos.Add(existing);
        }
        else if (!string.IsNullOrWhiteSpace(releaseNotes))
        {
            existing.ReleaseNotes = releaseNotes;
        }

        existing.InstallerFileName = fileName;
        existing.InstallerSha256 = hash;
        existing.InstallerSizeBytes = new FileInfo(path).Length;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Installer uploaded for version {Version}: {File} ({Size:N0} bytes, sha256 {Hash})",
            safeVersion, fileName, existing.InstallerSizeBytes, hash);

        return Ok(ApiResponse<object>.Ok(new
        {
            version = safeVersion,
            fileName,
            sha256 = hash,
            sizeBytes = existing.InstallerSizeBytes,
            approvedForRollout = existing.ApprovedForRollout,
            note = "Uploaded. Approve it on the Updates page before branches will take it.",
        }));
    }

    /// <summary>
    /// What a branch should be running, and how to fetch and verify it. Anonymous because a
    /// branch asks this before it has done anything else, and it discloses only a version
    /// number and a hash.
    /// </summary>
    [HttpGet("latest")]
    [AllowAnonymous]
    public async Task<IActionResult> Latest()
    {
        var version = await _db.VersionInfos
            .Where(v => v.ApprovedForRollout && v.InstallerFileName != null && v.InstallerSha256 != null)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        if (version is null)
            return Ok(ApiResponse<object>.Ok(new { available = false }));

        return Ok(ApiResponse<object>.Ok(new
        {
            available = true,
            version = version.CurrentVersion,
            releaseNotes = version.ReleaseNotes,
            sha256 = version.InstallerSha256,
            sizeBytes = version.InstallerSizeBytes,
            downloadPath = $"/api/releases/download/{version.CurrentVersion}",
        }));
    }

    /// <summary>Streams the installer for an approved version.</summary>
    [HttpGet("download/{version}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(string version)
    {
        var record = await _db.VersionInfos.FirstOrDefaultAsync(v => v.CurrentVersion == version);

        if (record is null || record.InstallerFileName is null)
            return NotFound(ApiResponse<object>.Fail("No installer is held for that version."));

        // An unapproved build must not be reachable just because someone knows its number.
        // Approval is the gate that decides what branches are allowed to run.
        if (!record.ApprovedForRollout)
            return StatusCode(403, ApiResponse<object>.Fail("That version has not been approved for rollout."));

        var path = Path.Combine(ReleaseDirectory, record.InstallerFileName);
        if (!System.IO.File.Exists(path))
        {
            _logger.LogError("Version {Version} is approved but its installer is missing at {Path}",
                version, path);
            return NotFound(ApiResponse<object>.Fail("The installer file is missing on the server."));
        }

        return PhysicalFile(path, "application/octet-stream", record.InstallerFileName, enableRangeProcessing: true);
    }
}
