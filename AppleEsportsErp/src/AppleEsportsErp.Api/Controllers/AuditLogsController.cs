using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Application.DTOs.Settings;
using AppleEsportsErp.Application.DTOs.Common;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// SOP §22: Immutable Audit Trail API.
///
/// The record itself was never the gap - AuthService, SessionService, BillingService,
/// WalletService, MemberService and half a dozen others have written a row here for every
/// login, session, payment, wallet change and edit for as long as this system has existed.
/// What was missing was a way to actually read it: no filters beyond branch, no total count to
/// build a page count from, and a branch's own rows never left that branch at all - so "what
/// happened at Katargam this afternoon" could only be answered by driving to Katargam. This is
/// the endpoint the new Activity Log screen reads, now that both of those are fixed.
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditLogsController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Every action Head Office knows about, filtered down to what somebody is actually
    /// looking for.
    ///
    /// Every filter is optional and they combine - "just Katargam", "just today", "just what
    /// Priya did", or all three together. None of this existed before; the old endpoint
    /// returned the newest 100 rows in the whole company with no way to narrow it, which is
    /// unusable the moment there is more than one branch and more than a few days of history.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "Dashboard:settings")]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? branchId = null,
        [FromQuery] string? userName = null,
        [FromQuery] string? action = null,
        [FromQuery] bool? failedOnly = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _unitOfWork.Repository<AuditLog>().Query();

        if (branchId.HasValue)
            query = query.Where(a => a.BranchId == branchId.Value);

        if (failedOnly == true)
            query = query.Where(a => !a.Success);

        // A contains match on the name someone typed, not an exact one - "priya" should find
        // "Priya Patel" without the caller needing to know how the name is capitalised or
        // spelled in full. Case-insensitivity here relies on PostgreSQL's default collation,
        // same as everywhere else in this codebase that searches a name.
        if (!string.IsNullOrWhiteSpace(userName))
            query = query.Where(a => a.UserName.Contains(userName));

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (from.HasValue)
            query = query.Where(a => a.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.CreatedAt <= to.Value);

        var total = await query.CountAsync();

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto
            {
                Id = a.Id,
                UserName = a.UserName,
                UserRole = a.UserRole,
                Action = a.Action,
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                Success = a.Success,
                Details = a.Details,
                IpAddress = a.IpAddress,
                CreatedAt = a.CreatedAt,
                BranchId = a.BranchId,
                // The row's own BranchName is a snapshot from when it was written, and is kept
                // rather than joined fresh - a branch that gets renamed later must not rewrite
                // history, and this way a row still names its branch correctly even if that
                // branch is deleted afterwards.
                BranchName = a.BranchName,
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(new PaginatedResult<AuditLogDto>(logs, total, page, pageSize)));
    }

    /// <summary>Every distinct action type Head Office has ever recorded, for the filter dropdown.</summary>
    [HttpGet("actions")]
    [Authorize(Policy = "Dashboard:settings")]
    public async Task<IActionResult> GetDistinctActions()
    {
        var actions = await _unitOfWork.Repository<AuditLog>().Query()
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(actions));
    }

    /// <summary>Every branch that has ever written a row, for the filter dropdown.</summary>
    [HttpGet("branches")]
    [Authorize(Policy = "Dashboard:settings")]
    public async Task<IActionResult> GetDistinctBranches()
    {
        var branches = await _unitOfWork.Repository<AuditLog>().Query()
            .Where(a => a.BranchId != null)
            .Select(a => new { a.BranchId, a.BranchName })
            .Distinct()
            .OrderBy(a => a.BranchName)
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(branches));
    }

    [HttpGet("branch")]
    [BranchIsolation]
    public async Task<IActionResult> GetBranchLogs([FromQuery] Guid? branchId = null, [FromQuery] int limit = 100, [FromQuery] int offset = 0)
    {
        var targetBranchId = branchId;
        if (targetBranchId == null && HttpContext.Items.TryGetValue("BranchId", out var itemVal) && itemVal != null)
        {
            targetBranchId = Guid.Parse(itemVal.ToString()!);
        }

        if (targetBranchId == null)
        {
            var assignedBranch = User.FindFirst("branchId")?.Value;
            if (!string.IsNullOrEmpty(assignedBranch))
            {
                targetBranchId = Guid.Parse(assignedBranch);
            }
        }

        if (targetBranchId == null)
        {
            return BadRequest(ApiResponse<object>.Fail("Branch context required."));
        }

        var query = _unitOfWork.Repository<AuditLog>().Query()
            .Where(a => a.BranchId == targetBranchId.Value);

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        var dtos = logs.Select(a => new AuditLogDto
        {
            Id = a.Id,
            UserName = a.UserName,
            UserRole = a.UserRole,
            Action = a.Action,
            TargetType = a.TargetType,
            TargetId = a.TargetId,
            Details = a.Details,
            IpAddress = a.IpAddress,
            CreatedAt = a.CreatedAt,
            BranchId = a.BranchId,
            BranchName = a.BranchName,
        });

        return Ok(ApiResponse<object>.Ok(dtos));
    }
}
