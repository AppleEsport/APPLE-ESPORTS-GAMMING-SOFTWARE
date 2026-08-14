using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Api.Filters;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Application.DTOs.Members;
using AppleEsportsErp.Application.Interfaces;
using System.Security.Claims;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/members")]
[Authorize]
[BranchIsolation]
public class MembersController : ControllerBase
{
    private readonly IMemberService _memberService;

    public MembersController(IMemberService memberService)
    {
        _memberService = memberService;
    }

    private Guid GetBranchId() 
    {
        var val = HttpContext.Items["BranchId"]?.ToString();
        return string.IsNullOrEmpty(val) ? Guid.Empty : Guid.Parse(val);
    }

    [HttpGet]
    public async Task<IActionResult> GetMembers([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] bool includeDeleted = false)
    {
        var result = await _memberService.GetMembersAsync(GetBranchId(), search, page, pageSize, includeDeleted);
        return Ok(ApiResponse<PaginatedResult<MemberDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetMemberById(Guid id)
    {
        var result = await _memberService.GetMemberByIdAsync(id);
        return Ok(ApiResponse<MemberDto>.Ok(result));
    }

    [HttpGet("phone/{mobileNumber}")]
    public async Task<IActionResult> GetMemberByMobile(string mobileNumber)
    {
        var result = await _memberService.GetMemberByMobileAsync(mobileNumber);
        return Ok(ApiResponse<MemberDto>.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> RegisterMember([FromBody] RegisterMemberDto dto)
    {
        var result = await _memberService.RegisterMemberAsync(GetBranchId(), (await this.GetOperatorIdAsync()), dto);
        return Ok(ApiResponse<MemberDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateMember(Guid id, [FromBody] UpdateMemberDto dto)
    {
        // A debug Console.WriteLine used to print every member's name and login details here on
        // every edit, straight into the branch's service log where they sat in plain text.
        var result = await _memberService.UpdateMemberAsync(GetBranchId(), (await this.GetOperatorIdAsync()), id, dto);
        return Ok(ApiResponse<MemberDto>.Ok(result));
    }

    /// <summary>
    /// Removes a member. Super Admin only.
    ///
    /// It was open to any signed-in operator, which put the single most destructive action in
    /// the system on the same screen as everyday counter work. A member carries a wallet
    /// balance, a credit history and a share of every End of Day they have ever appeared in, so
    /// removing one is not like clearing a typo - and a counter is a busy place where the wrong
    /// row gets clicked. There is also no branch check on the way in, so an operator at one shop
    /// could remove a member who belongs to another.
    ///
    /// Nothing legitimate is lost by restricting it. Suspending a member is the operator-level
    /// action for somebody who should not be playing, it is reversible, and it keeps the money
    /// trail intact.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.SuperAdmin)]
    public async Task<IActionResult> DeleteMember(Guid id)
    {
        await _memberService.DeleteMemberAsync(GetBranchId(), (await this.GetOperatorIdAsync()), id);
        return Ok(ApiResponse<object>.Ok(null));
    }

    /// <summary>Super Admin only: directly override any wallet balance / lifetime stat on a member's profile.</summary>
    [HttpPut("{id:guid}/admin-edit")]
    [Authorize(Policy = "Dashboard:member_value_edit")]
    public async Task<IActionResult> AdminEditValues(Guid id, [FromBody] AdminEditMemberValuesDto dto)
    {
        var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await _memberService.AdminEditValuesAsync(GetBranchId(), adminId, id, dto);
        return Ok(ApiResponse<MemberDto>.Ok(result));
    }

    /// <summary>Member self-login — POST /api/members/login (no auth required)</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> MemberLogin([FromBody] MemberLoginDto dto)
    {
        var result = await _memberService.LoginMemberAsync(dto);
        return Ok(ApiResponse<MemberLoginResponseDto>.Ok(result));
    }
}

