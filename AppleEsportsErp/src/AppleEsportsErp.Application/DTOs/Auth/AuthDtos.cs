using AppleEsportsErp.Application.DTOs.Shift;

namespace AppleEsportsErp.Application.DTOs.Auth;

/// <summary>SOP §6.2: Super Admin Login request</summary>
public class AdminLoginDto
{
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public DeviceInfoDto? DeviceInfo { get; set; }
}

/// <summary>SOP §6.3: Operator Login request — Branch → Profile → PIN</summary>
public class OperatorLoginDto
{
    public Guid BranchId { get; set; }
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public DeviceInfoDto? DeviceInfo { get; set; }
}

/// <summary>Login response with tokens and user profile</summary>
public class LoginResponseDto
{
    public UserProfileDto User { get; set; } = null!;
    public string AccessToken { get; set; } = null!;
    public string RefreshToken { get; set; } = null!;

    /// <summary>
    /// True when this login picked up a shift that was still open rather than starting a new
    /// one - which means the last one was never closed properly. Usually a power cut or a PC
    /// switched off with the app running.
    /// </summary>
    public bool ResumedShift { get; set; }

    /// <summary>
    /// How long that shift sat unattended, in minutes. Only meaningful with ResumedShift.
    /// </summary>
    public int UnattendedMinutes { get; set; }

    /// <summary>
    /// True when the gap is long enough to be worth explaining. The operator is asked what
    /// happened, and nothing is reported to the owner until they answer - guessing would mean
    /// a power-cut email every morning somebody simply shut the shop without ending a shift.
    /// </summary>
    public bool NeedsGapExplanation { get; set; }

    /// <summary>
    /// Somebody else's shift, still open at this branch, that has to be closed and counted
    /// before this operator can start. Null on a normal login.
    ///
    /// When this is set, <see cref="UserProfileDto.ShiftId"/> is null: the login succeeded but
    /// no shift was opened, and none will be until the drawer has been counted.
    /// </summary>
    public PendingTakeoverDto? PendingTakeover { get; set; }
}

/// <summary>User profile — returned on login and GET /me</summary>
public class UserProfileDto
{
    public Guid Id { get; set; }
    public string? Email { get; set; }
    public string FullName { get; set; } = null!;
    public string? Username { get; set; }
    public string Role { get; set; } = null!;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
    public Guid? ShiftId { get; set; }
    public object? DashboardPermissions { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? LastLogin { get; set; }
    public ActiveShiftDto? ActiveShift { get; set; }
}

public class ActiveShiftDto
{
    public Guid Id { get; set; }
    public DateTimeOffset LoginTime { get; set; }
}

public class RefreshTokenDto
{
    public string RefreshToken { get; set; } = null!;
}

public class TokenResponseDto
{
    public string AccessToken { get; set; } = null!;
}

public class ForceLogoutResponseDto
{
    public bool Success { get; set; }
    public string Operator { get; set; } = null!;
}

public class DeviceInfoDto
{
    public string? UserAgent { get; set; }
    public string? Platform { get; set; }
    public string? DeviceId { get; set; }
}

/// <summary>Branch list item for login selection screen</summary>
public class BranchListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Address { get; set; }
    public string Status { get; set; } = null!;
    public string OpeningTime { get; set; } = "10:00";
    public string ClosingTime { get; set; } = "02:00";
}

/// <summary>Operator list item for login selection screen</summary>
public class OperatorListItemDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Status { get; set; } = null!;
}

public class VerifyAdminDto
{
    public string Password { get; set; } = null!;
}

/// <summary>Member Login request from PC Overlay</summary>
public class MemberLoginDto
{
    public string Identifier { get; set; } = null!; // Can be Username, Email, or MobileNumber
    public string Password { get; set; } = null!;
    public DeviceInfoDto? DeviceInfo { get; set; }
}

public class ForgotPasswordDto
{
    public string Email { get; set; } = null!;
    // "member" or "staff" — which login screen the request came from. The same email can
    // belong to both a Member and an Operator/User account, so without this the reset
    // always landed on whichever account type was checked first, silently resetting the
    // wrong one. Null falls back to the old any-match behavior for callers that don't send it.
    public string? AccountType { get; set; }
}

public class ResetPasswordDto
{
    public string Email { get; set; } = null!;
    public string Token { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
    public string? AccountType { get; set; }
}

public class ChangeCredentialsDto
{
    public Guid UserId { get; set; }
    public string? NewUsername { get; set; }
    public string? NewEmail { get; set; }
    public string? NewPassword { get; set; }
}

public class SetupMasterDto
{
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string FullName { get; set; } = "System Administrator";
}

public class SetupOperatorDto
{
    public string Email { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public Guid BranchId { get; set; }
}

public class CheckSetupResponseDto
{
    public bool NeedsSuperAdminSetup { get; set; }
    public bool NeedsOperatorSetup { get; set; }
    public bool NeedsAdminSetup { get; set; }
}

public class AdminSwitchInDto
{
    public Guid AdminId { get; set; }
    public string AccessPin { get; set; } = null!;
    public Guid ShiftId { get; set; }
}

public class AvailableAdminDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = null!;
    public string Type { get; set; } = null!; // "Admin" or "Operator"
    public int PinLength { get; set; }
}

/// <summary>Body of POST /api/auth/logout. Optional - a plain logout sends nothing.</summary>
public class LogoutDto
{
    /// <summary>
    /// The operator ticked "this is the last shift of the day". Only they know whether
    /// another shift follows, and guessing it from the clock is what would make every
    /// nightly close look like a power cut.
    /// </summary>
    public bool ClosesTradingDay { get; set; }
}
