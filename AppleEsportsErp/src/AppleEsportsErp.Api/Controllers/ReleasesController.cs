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
/// Where a branch fetches the installer for a new version, and where Head Office publishes one.
///
/// The tracking half of updates has existed since Phase 1: a version can be created, approved,
/// and every branch's progress watched. The delivery half did not. The dashboard could say
/// "2.2.0 is ready" and pressing Update Now had nothing to fetch, because nothing packaged a
/// build, nothing hosted it, and nothing on a branch could download it. Every fix therefore
/// meant somebody walking to each counter with a USB stick and reinstalling.
///
/// The branch side was already written and is recovered intact - it checks, downloads, verifies
/// a SHA-256 and installs. It was asking <c>/api/releases/latest</c>, which did not exist; the
/// server only had <c>/api/versions/latest</c>, a different route with a different shape. So the
/// client got a 404 every time and silently concluded there was no update. That is this file.
/// </summary>
[ApiController]
[Route("api/releases")]
public class ReleasesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ReleasesController> _logger;

    public ReleasesController(AppDbContext db, IWebHostEnvironment env, ILogger<ReleasesController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Where published installers are kept. Outside the application directory on purpose: an
    /// upgrade replaces the application, and anything stored inside it goes with it.
    /// </summary>
    private string ReleaseFolder
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("Releases__Path");
            var path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Path.GetPathRoot(_env.ContentRootPath) ?? "/", "releases")
                : configured;
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>
    /// What a branch asks for, on its own schedule.
    ///
    /// Anonymous, deliberately. A branch checks for updates as a machine rather than as a
    /// logged-in person, and there may be nobody signed in at the counter at 4am. It gives away
    /// nothing but a version number and a hash — both of which a branch has to be told anyway
    /// before it can verify what it downloads.
    /// </summary>
    [HttpGet("latest")]
    [AllowAnonymous]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        var version = await _db.Set<VersionInfo>()
            .Where(v => v.ApprovedForRollout)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Nothing approved, or approved but never packaged. Both mean "no update to take", and
        // both must say so plainly rather than offering something a branch cannot fetch.
        if (version is null
            || string.IsNullOrWhiteSpace(version.InstallerFileName)
            || string.IsNullOrWhiteSpace(version.InstallerSha256))
        {
            return Ok(ApiResponse<object>.Ok(new { available = false }));
        }

        // Approved and recorded, but the file is not actually on disk — a database restored
        // without its releases, or an upload that failed halfway. Offering it would send every
        // branch to a download that 404s, on repeat.
        if (!System.IO.File.Exists(Path.Combine(ReleaseFolder, version.InstallerFileName)))
        {
            _logger.LogError(
                "Version {Version} is approved and has an installer recorded, but {FileName} is not on disk. " +
                "Branches are being told there is no update, which is better than sending them to a 404.",
                version.CurrentVersion, version.InstallerFileName);
            return Ok(ApiResponse<object>.Ok(new { available = false }));
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            available = true,
            version = version.CurrentVersion,
            releaseNotes = version.ReleaseNotes ?? string.Empty,
            sha256 = version.InstallerSha256,
            sizeBytes = version.InstallerSizeBytes,
            downloadPath = $"/api/releases/download/{version.InstallerFileName}",
        }));
    }

    /// <summary>
    /// Hands over the installer itself.
    ///
    /// The branch checks the SHA-256 against what <see cref="Latest"/> published before running
    /// anything, so this endpoint is not what makes the download trustworthy — the hash is. It
    /// still refuses anything that is not a plain file name, because a path that walks upward
    /// would serve any file on the machine to anyone who asked.
    /// </summary>
    [HttpGet("download/{fileName}")]
    [AllowAnonymous]
    public IActionResult Download(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName != Path.GetFileName(fileName)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse<object>.Fail("That is not a release file name.", "BAD_FILE_NAME"));
        }

        var path = Path.Combine(ReleaseFolder, fileName);
        if (!System.IO.File.Exists(path))
            return NotFound(ApiResponse<object>.Fail("No such release.", "RELEASE_NOT_FOUND"));

        // Streamed rather than read into memory: these are 150 MB and up, and four branches may
        // ask at once.
        return PhysicalFile(path, "application/octet-stream", fileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Publishes an installer against a version, and records its hash.
    ///
    /// The hash is computed here from the bytes actually stored, never taken from whoever
    /// uploaded. A hash supplied alongside the file proves nothing — the same person could send
    /// both.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Roles = Roles.SuperAdmin)]
    [RequestSizeLimit(1_073_741_824)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string version, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse<object>.Fail("No installer was uploaded.", "NO_FILE"));

        if (string.IsNullOrWhiteSpace(version))
            return BadRequest(ApiResponse<object>.Fail("Which version is this installer for?", "NO_VERSION"));

        var record = await _db.Set<VersionInfo>()
            .FirstOrDefaultAsync(v => v.CurrentVersion == version, ct);

        if (record is null)
            return NotFound(ApiResponse<object>.Fail(
                $"There is no version {version} to attach this to. Create it first.", "VERSION_NOT_FOUND"));

        var safeName = Path.GetFileName(file.FileName);
        if (!safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse<object>.Fail("A release must be an .exe installer.", "NOT_AN_INSTALLER"));

        var target = Path.Combine(ReleaseFolder, safeName);

        // Written to a temporary name and moved into place, so a branch checking at the wrong
        // moment cannot download a half-written installer and fail its hash check.
        var staging = target + ".part";
        await using (var stream = System.IO.File.Create(staging))
        {
            await file.CopyToAsync(stream, ct);
        }

        string hash;
        await using (var stream = System.IO.File.OpenRead(staging))
        {
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }

        System.IO.File.Move(staging, target, overwrite: true);

        record.InstallerFileName = safeName;
        record.InstallerSha256 = hash;
        record.InstallerSizeBytes = file.Length;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Published installer {FileName} ({SizeMb:N0} MB) for version {Version}. SHA-256 {Hash}.",
            safeName, file.Length / 1024d / 1024d, version, hash);

        return Ok(ApiResponse<object>.Ok(new
        {
            version = record.CurrentVersion,
            fileName = safeName,
            sha256 = hash,
            sizeBytes = file.Length,
            approved = record.ApprovedForRollout,
        }));
    }
}
