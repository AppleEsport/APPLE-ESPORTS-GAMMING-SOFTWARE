using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Shift;
using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Closing a shift that its own operator never closed.
///
/// Deliberately not shift-scoped: the operator calling these has no shift yet, and that is the
/// point. Login gives them credentials and nothing else while somebody else's uncounted drawer
/// is still open at their branch. A shift is issued by <c>count</c> or <c>confirm</c>, and only
/// once the money in front of them is on record.
/// </summary>
[ApiController]
[Route("api/shift-takeover")]
[Authorize]
[BranchIsolation]
public class ShiftTakeoverController : ControllerBase
{
    private readonly IShiftTakeoverService _takeover;

    public ShiftTakeoverController(IShiftTakeoverService takeover)
    {
        _takeover = takeover;
    }

    private Guid GetBranchId() => Guid.Parse(HttpContext.Items["BranchId"]!.ToString()!);

    private Guid GetOperatorId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>What this operator has to deal with before they can start, if anything.</summary>
    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var pending = await _takeover.GetPendingAsync(GetBranchId(), GetOperatorId(), ct);
        return Ok(ApiResponse<PendingTakeoverDto?>.Ok(pending));
    }

    /// <summary>
    /// The blind count. Recorded before the expected figures come back in the response, so it
    /// cannot be revised to agree with them.
    /// </summary>
    [HttpPost("count")]
    public async Task<IActionResult> Count([FromBody] SubmitTakeoverCountDto dto, CancellationToken ct)
    {
        var result = await _takeover.SubmitCountAsync(GetBranchId(), GetOperatorId(), dto, ct);
        return Ok(ApiResponse<TakeoverCountResultDto>.Ok(result));
    }

    /// <summary>The explanation for a difference, which closes the old shift and starts the new one.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ConfirmTakeoverDto dto, CancellationToken ct)
    {
        var result = await _takeover.ConfirmAsync(GetBranchId(), GetOperatorId(), dto, ct);
        return Ok(ApiResponse<TakeoverCompletedDto>.Ok(result));
    }
}
