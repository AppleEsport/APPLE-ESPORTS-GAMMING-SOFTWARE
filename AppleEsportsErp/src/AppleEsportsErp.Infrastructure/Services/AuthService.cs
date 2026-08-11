using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.DTOs.Auth;
using AppleEsportsErp.Application.Exceptions;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Infrastructure.Identity;
using BCryptNet = BCrypt.Net.BCrypt;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// Full AuthService implementation — 1:1 mapping from auth.service.js.
/// SOP §6: Login System (Admin + Operator flows)
/// SOP §21: Security Model (hashing, tokens, device tracking)
/// SOP §10: Shift Management (auto shift start on login)
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly IAuditService _audit;
    private readonly ILogger<AuthService> _logger;
    private readonly ITokenRevocationService _tokenRevocation;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IAppUrlProvider _appUrls;
    private const int SALT_ROUNDS = 12;

    // Brute-force lockout — SOP-adjacent security hardening: 5 wrong passwords locks the
    // account for 15 minutes and auto-emails a reset link so a legitimate user isn't stuck
    // waiting out the clock.
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IAdminNotifier _adminNotifier;

    public AuthService(AppDbContext db, JwtTokenService jwt, IAuditService audit, ILogger<AuthService> logger, ITokenRevocationService tokenRevocation, IEmailService emailService, IConfiguration configuration, IAppUrlProvider appUrls, IAdminNotifier adminNotifier)
    {
        _adminNotifier = adminNotifier;
        _db = db;
        _jwt = jwt;
        _audit = audit;
        _logger = logger;
        _tokenRevocation = tokenRevocation;
        _emailService = emailService;
        _configuration = configuration;
        _appUrls = appUrls;
    }

    /// <summary>
    /// SOP §6.2: Super Admin Login — Email/Password → Validate credentials, permissions, device, account status.
    /// SOP: Super Admin session persists until logout/timeout/password reset/forced signout.
    /// Maps from: auth.service.js loginAdmin()
    /// </summary>
    public async Task<LoginResponseDto> LoginAdminAsync(AdminLoginDto dto)
    {
        // 1. Find admin user
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
        if (user != null)
        {
            // 2. Check account status
            if (user.Status != UserStatus.Active)
                throw new AuthorizationException($"Account is {user.Status}. Contact system administrator.", "ACCOUNT_INACTIVE");

            // 2b. Brute-force lockout
            if (IsLocked(user.LockedUntil))
                throw new AuthorizationException(LockoutMessage(user.LockedUntil!.Value), "ACCOUNT_LOCKED");

            // 3. Verify password — SOP §21.1: Password Hashing = YES
            if (!BCryptNet.Verify(dto.Password, user.PasswordHash))
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= MaxFailedAttempts)
                {
                    user.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                    var resetToken = Guid.NewGuid().ToString("N");
                    user.ResetToken = resetToken;
                    user.ResetTokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                    await _db.SaveChangesAsync();
                    await SendPasswordResetEmailAsync(user.Email, user.FullName, resetToken, isLockout: true);
                    await _audit.LogAsync(new AuditEntry
                    {
                        UserId = user.Id,
                        UserRole = Roles.SuperAdmin,
                        UserName = user.FullName,
                        Action = AuditActions.AccountLocked,
                        TargetType = "user",
                        TargetId = user.Id,
                        Details = new { reason = "5 failed password attempts", lockedUntil = user.LockedUntil },
                    });
                }
                else
                {
                    await _db.SaveChangesAsync();
                }

                // Log failed attempt — SOP §22: Audit every critical action
                await _audit.LogAsync(new AuditEntry
                {
                    UserId = user.Id,
                    UserRole = Roles.SuperAdmin,
                    UserName = user.FullName,
                    Action = AuditActions.FailedLogin,
                    Details = new { reason = "Invalid password", deviceInfo = dto.DeviceInfo },
                });
                throw new AuthenticationException("Invalid email/username or password", "INVALID_CREDENTIALS");
            }

            // Successful login clears the lockout counter
            user.FailedAttempts = 0;
            user.LockedUntil = null;

            // 4. Generate tokens — Q1 Decision: full claims embedded in JWT
            var claims = new Dictionary<string, string>
            {
                [ClaimTypes.NameIdentifier] = user.Id.ToString(),
                [ClaimTypes.Role] = user.Role,
                [ClaimTypes.Name] = user.FullName,
            };

            if (user.Role == Roles.Admin && !string.IsNullOrEmpty(user.DashboardPermissions))
            {
                claims["dashboardPermissions"] = user.DashboardPermissions;
            }

            var accessToken = _jwt.GenerateAccessToken(claims);
            var refreshToken = _jwt.GenerateRefreshToken(claims);

            // 5. Update last login + device info — SOP §21.1: Device Tracking
            user.LastLogin = DateTimeOffset.UtcNow;
            user.DeviceInfo = dto.DeviceInfo != null ? JsonSerializer.Serialize(dto.DeviceInfo) : null;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            // 6. Audit log — SOP §22
            await _audit.LogAsync(new AuditEntry
            {
                UserId = user.Id,
                UserRole = user.Role,
                UserName = user.FullName,
                Action = AuditActions.Login,
                Details = new { method = "email_password", deviceInfo = dto.DeviceInfo },
            });

            _logger.LogInformation("{Role} logged in: {Name}", user.Role, user.FullName);

            return new LoginResponseDto
            {
                User = new UserProfileDto
                {
                    Id = user.Id,
                    Email = user.Email,
                    FullName = user.FullName,
                    Role = user.Role,
                    DashboardPermissions = user.DashboardPermissions != null 
                        ? JsonSerializer.Deserialize<object>(user.DashboardPermissions) 
                        : null,
                    Status = user.Status.ToString().ToLowerInvariant(),
                    LastLogin = user.LastLogin,
                },
                AccessToken = accessToken,
                RefreshToken = refreshToken,
            };
        }

        // 1b. Fallback: check if it's an Operator promoted to Global Admin
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.Username == dto.Email && o.IsGlobalAdmin);
        if (op != null)
        {
            if (op.Status != OperatorStatus.Active)
                throw new AuthorizationException($"Account is {op.Status}. Contact system administrator.", "ACCOUNT_INACTIVE");

            if (IsLocked(op.LockedUntil))
                throw new AuthorizationException(LockoutMessage(op.LockedUntil!.Value), "ACCOUNT_LOCKED");

            if (!BCryptNet.Verify(dto.Password, op.PasswordHash))
            {
                op.FailedAttempts++;
                if (op.FailedAttempts >= MaxFailedAttempts)
                {
                    op.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                    var resetToken = Guid.NewGuid().ToString("N");
                    op.ResetToken = resetToken;
                    op.ResetTokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                    await _db.SaveChangesAsync();
                    await SendPasswordResetEmailAsync(op.Email, op.FullName, resetToken, isLockout: true);
                    await _audit.LogAsync(new AuditEntry
                    {
                        OperatorId = op.Id,
                        UserRole = Roles.Admin,
                        UserName = op.FullName,
                        Action = AuditActions.AccountLocked,
                        TargetType = "operator",
                        TargetId = op.Id,
                        Details = new { reason = "5 failed password attempts", lockedUntil = op.LockedUntil },
                    });
                }
                else
                {
                    await _db.SaveChangesAsync();
                }

                await _audit.LogAsync(new AuditEntry
                {
                    OperatorId = op.Id,
                    UserRole = Roles.Admin,
                    UserName = op.FullName,
                    Action = AuditActions.FailedLogin,
                    Details = new { reason = "Invalid password", deviceInfo = dto.DeviceInfo },
                });
                throw new AuthenticationException("Invalid email/username or password", "INVALID_CREDENTIALS");
            }

            op.FailedAttempts = 0;
            op.LockedUntil = null;

            var claims = new Dictionary<string, string>
            {
                [ClaimTypes.NameIdentifier] = op.Id.ToString(),
                [ClaimTypes.Role] = Roles.Admin,
                [ClaimTypes.Name] = op.FullName,
            };

            if (!string.IsNullOrEmpty(op.DashboardPermissions))
            {
                claims["dashboardPermissions"] = op.DashboardPermissions;
            }

            var accessToken = _jwt.GenerateAccessToken(claims);
            var refreshToken = _jwt.GenerateRefreshToken(claims);

            op.LastLogin = DateTimeOffset.UtcNow;
            op.DeviceInfo = dto.DeviceInfo != null ? JsonSerializer.Serialize(dto.DeviceInfo) : null;
            op.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();

            await _audit.LogAsync(new AuditEntry
            {
                OperatorId = op.Id,
                UserRole = Roles.Admin,
                UserName = op.FullName,
                Action = AuditActions.Login,
                Details = new { method = "username_password_admin_fallback", deviceInfo = dto.DeviceInfo },
            });

            _logger.LogInformation("{Role} logged in: {Name} (Operator Fallback)", Roles.Admin, op.FullName);

            return new LoginResponseDto
            {
                User = new UserProfileDto
                {
                    Id = op.Id,
                    Email = op.Username, // Map username to email field for frontend compatibility
                    FullName = op.FullName,
                    Role = Roles.Admin,
                    DashboardPermissions = op.DashboardPermissions != null 
                        ? JsonSerializer.Deserialize<object>(op.DashboardPermissions) 
                        : null,
                    Status = op.Status.ToString().ToLowerInvariant(),
                    LastLogin = op.LastLogin,
                },
                AccessToken = accessToken,
                RefreshToken = refreshToken,
            };
        }

        throw new AuthenticationException("Invalid email/username or password", "INVALID_CREDENTIALS");
    }

    /// <summary>
    /// SOP §6.3: Operator Login — Select Branch → Select Profile → Enter PIN → System starts shift.
    /// SOP: Operator CANNOT see other branch data (enforced via branch assignment check).
    /// Maps from: auth.service.js loginOperator()
    /// </summary>
    public async Task<LoginResponseDto> LoginOperatorAsync(OperatorLoginDto dto)
    {
        // 1. Verify branch exists and is active
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.Id == dto.BranchId);
        if (branch == null)
            throw new NotFoundException("Branch not found", "BRANCH_NOT_FOUND");
        if (branch.Status != BranchStatus.Active)
            throw new AuthorizationException("Branch is currently inactive", "BRANCH_INACTIVE");

        // 2. Find operator assigned to this branch
        var op = await _db.Operators.FirstOrDefaultAsync(
            o => o.Username == dto.Username && o.BranchId == dto.BranchId);
        if (op == null)
            throw new AuthenticationException("Invalid credentials or operator not assigned to this branch", "INVALID_CREDENTIALS");

        // 3. Check operator status — SOP §12: Operator Status Types
        if (op.Status == OperatorStatus.Suspended)
            throw new AuthorizationException("Operator account is suspended. Contact Super Admin.", "OPERATOR_SUSPENDED");
        if (op.Status == OperatorStatus.Disabled)
            throw new AuthorizationException("Operator account is disabled. Contact Super Admin.", "OPERATOR_DISABLED");

        // 3b. Brute-force lockout
        if (IsLocked(op.LockedUntil))
            throw new AuthorizationException(LockoutMessage(op.LockedUntil!.Value), "ACCOUNT_LOCKED");

        // 4. Verify password/PIN
        if (!BCryptNet.Verify(dto.Password, op.PasswordHash))
        {
            op.FailedAttempts++;
            if (op.FailedAttempts >= MaxFailedAttempts)
            {
                op.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                var resetToken = Guid.NewGuid().ToString("N");
                op.ResetToken = resetToken;
                op.ResetTokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                await _db.SaveChangesAsync();
                await SendPasswordResetEmailAsync(op.Email, op.FullName, resetToken, isLockout: true);
                await _audit.LogAsync(new AuditEntry
                {
                    OperatorId = op.Id,
                    UserRole = Roles.Operator,
                    UserName = op.FullName,
                    Action = AuditActions.AccountLocked,
                    BranchId = dto.BranchId,
                    BranchName = branch.Name,
                    TargetType = "operator",
                    TargetId = op.Id,
                    Details = new { reason = "5 failed password attempts", lockedUntil = op.LockedUntil },
                });
            }
            else
            {
                await _db.SaveChangesAsync();
            }

            await _audit.LogAsync(new AuditEntry
            {
                OperatorId = op.Id,
                UserRole = Roles.Operator,
                UserName = op.FullName,
                Action = AuditActions.FailedLogin,
                BranchId = dto.BranchId,
                BranchName = branch.Name,
                Details = new { reason = "Invalid password/PIN", deviceInfo = dto.DeviceInfo },
            });
            throw new AuthenticationException("Invalid password or PIN", "INVALID_CREDENTIALS");
        }

        op.FailedAttempts = 0;
        op.LockedUntil = null;

        // 5. Resume this operator's open shift if they already have one, rather than opening a
        //    second. A shift only ends by being closed properly, with the cash counted - so an
        //    operator whose PC lost power mid-shift comes back to a shift still marked Active.
        //    Starting a fresh one would leave the first open forever, with uncounted takings
        //    against it, and every login would add another. There is no counting your way out
        //    of that. Resuming also means the shop opens on time without ringing an admin.
        var shift = await _db.Shifts
            .Where(s => s.OperatorId == op.Id && s.Status == ShiftStatus.Active)
            .OrderByDescending(s => s.LoginTime)
            .FirstOrDefaultAsync();

        var resumedShift = shift is not null;
        var gapSinceLastSeen = TimeSpan.Zero;

        if (resumedShift)
        {
            // How long the shift was unattended, measured from the last thing that actually
            // happened on it rather than from login - a shift opened at 09:00 and abandoned at
            // 23:00 was not "14 hours idle".
            var lastActivity = await _db.Sessions
                .Where(s => s.ShiftId == shift!.Id)
                .MaxAsync(s => (DateTimeOffset?)s.UpdatedAt) ?? shift!.LoginTime;

            gapSinceLastSeen = DateTimeOffset.UtcNow - lastActivity;

            shift!.DeviceInfo = dto.DeviceInfo != null ? JsonSerializer.Serialize(dto.DeviceInfo) : shift.DeviceInfo;
            _logger.LogInformation(
                "Operator {Operator} resumed shift {ShiftId}, which had been unattended for {Gap}.",
                op.FullName, shift.Id, gapSinceLastSeen);
        }
        else
        {
            shift = new Shift
            {
                OperatorId = op.Id,
                BranchId = dto.BranchId,
                LoginTime = DateTimeOffset.UtcNow,
                DeviceInfo = dto.DeviceInfo != null ? JsonSerializer.Serialize(dto.DeviceInfo) : null,
                Status = ShiftStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.Shifts.Add(shift);
        }

        // 6. Update operator status to active
        op.Status = OperatorStatus.Active;
        op.LastLogin = DateTimeOffset.UtcNow;
        op.DeviceInfo = dto.DeviceInfo != null ? JsonSerializer.Serialize(dto.DeviceInfo) : null;
        op.IsOnline = true;
        op.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync();

        // 7. Generate tokens with branch + permissions embedded — Q1 Decision
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = op.Id.ToString(),
            [ClaimTypes.Role] = Roles.Operator,
            [ClaimTypes.Name] = op.FullName,
            ["branchId"] = op.BranchId.ToString(),
            ["shiftId"] = shift.Id.ToString(),
            ["dashboardPermissions"] = op.DashboardPermissions ?? "{}",
        };

        var accessToken = _jwt.GenerateAccessToken(claims);
        var refreshToken = _jwt.GenerateRefreshToken(claims);

        // 8. Audit log — login
        await _audit.LogAsync(new AuditEntry
        {
            OperatorId = op.Id,
            UserRole = Roles.Operator,
            UserName = op.FullName,
            Action = AuditActions.Login,
            BranchId = dto.BranchId,
            BranchName = branch.Name,
            Details = new { shiftId = shift.Id, loginTime = shift.LoginTime, deviceInfo = dto.DeviceInfo },
        });

        // 9. Shift start audit
        await _audit.LogAsync(new AuditEntry
        {
            OperatorId = op.Id,
            UserRole = Roles.Operator,
            UserName = op.FullName,
            Action = AuditActions.ShiftStart,
            BranchId = dto.BranchId,
            BranchName = branch.Name,
            Details = new { shiftId = shift.Id },
        });

        _logger.LogInformation("Operator logged in: {Name} @ {Branch}", op.FullName, branch.Name);

        return new LoginResponseDto
        {
            User = new UserProfileDto
            {
                Id = op.Id,
                FullName = op.FullName,
                Username = op.Username,
                Role = Roles.Operator,
                BranchId = op.BranchId,
                BranchName = branch.Name,
                ShiftId = shift.Id,
                DashboardPermissions = op.DashboardPermissions != null
                    ? JsonSerializer.Deserialize<object>(op.DashboardPermissions)
                    : null,
                Status = op.Status.ToString().ToLowerInvariant(),
                LastLogin = op.LastLogin,
                ActiveShift = new ActiveShiftDto { Id = shift.Id, LoginTime = shift.LoginTime },
            },
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ResumedShift = resumedShift,
            UnattendedMinutes = (int)gapSinceLastSeen.TotalMinutes,
            // Ten minutes, so a browser refresh or a quick re-login is not treated as an
            // incident, while a genuine outage or an overnight shutdown always is.
            NeedsGapExplanation = resumedShift && gapSinceLastSeen >= TimeSpan.FromMinutes(10),
        };
    }

    /// <summary>
    /// Member Login - Accessible via PC Client Overlay across any branch.
    /// Uses Username, MobileNumber or Email + Password.
    /// </summary>
    public async Task<LoginResponseDto> LoginMemberAsync(MemberLoginDto dto)
    {
        // Find member by Username, MobileNumber, or Email
        var member = await _db.Members.FirstOrDefaultAsync(m => 
            (m.Username != null && m.Username == dto.Identifier) || 
            (m.MobileNumber != null && m.MobileNumber == dto.Identifier) || 
            (m.Email != null && m.Email == dto.Identifier));

        if (member == null)
            throw new AuthenticationException("Invalid credentials", "INVALID_CREDENTIALS");

        if (member.Status != MemberStatus.Active)
            throw new AuthorizationException($"Account is {member.Status}. Please contact front desk.", "ACCOUNT_INACTIVE");

        if (IsLocked(member.LockedUntil))
            throw new AuthorizationException(LockoutMessage(member.LockedUntil!.Value), "ACCOUNT_LOCKED");

        if (string.IsNullOrEmpty(member.PasswordHash) || !BCryptNet.Verify(dto.Password, member.PasswordHash))
        {
            member.FailedAttempts++;
            if (member.FailedAttempts >= MaxFailedAttempts)
            {
                member.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                var resetToken = Guid.NewGuid().ToString("N");
                member.ResetToken = resetToken;
                member.ResetTokenExpiry = DateTimeOffset.UtcNow.AddHours(1);
                await _db.SaveChangesAsync();
                await SendPasswordResetEmailAsync(member.Email, member.FullName, resetToken, isLockout: true);
                await _audit.LogAsync(new AuditEntry
                {
                    UserRole = "Member",
                    UserName = member.FullName,
                    Action = AuditActions.AccountLocked,
                    TargetType = "member",
                    TargetId = member.Id,
                    Details = new { reason = "5 failed password attempts", lockedUntil = member.LockedUntil },
                });
            }
            else
            {
                await _db.SaveChangesAsync();
            }

            await _audit.LogAsync(new AuditEntry
            {
                UserRole = "Member",
                UserName = member.FullName,
                Action = AuditActions.FailedLogin,
                Details = new { reason = "Invalid password", deviceInfo = dto.DeviceInfo },
            });
            throw new AuthenticationException("Invalid credentials", "INVALID_CREDENTIALS");
        }

        member.FailedAttempts = 0;
        member.LockedUntil = null;

        // Generate JWT
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = member.Id.ToString(),
            [ClaimTypes.Role] = "Member",
            [ClaimTypes.Name] = member.FullName,
        };

        var accessToken = _jwt.GenerateAccessToken(claims);
        var refreshToken = _jwt.GenerateRefreshToken(claims);

        member.LastVisit = DateTimeOffset.UtcNow;
        member.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.LogAsync(new AuditEntry
        {
            UserRole = "Member",
            UserName = member.FullName,
            Action = AuditActions.Login,
            Details = new { deviceInfo = dto.DeviceInfo },
        });

        _logger.LogInformation("Member logged in: {Name}", member.FullName);

        return new LoginResponseDto
        {
            User = new UserProfileDto
            {
                Id = member.Id,
                Username = member.Username,
                FullName = member.FullName,
                Role = "Member",
                Status = member.Status.ToString().ToLowerInvariant(),
                LastLogin = member.LastVisit,
            },
            AccessToken = accessToken,
            RefreshToken = refreshToken,
        };
    }

    /// <summary>
    /// SOP §10: Logout — closes shift for operators.
    /// SOP: System records logout time, shift summary, revenue, actions.
    /// Maps from: auth.service.js logout()
    /// </summary>
    /// <summary>
    /// The day's figures, emailed to the owner when the last shift closes.
    ///
    /// Counted over the 06:00-06:00 trading day rather than the shift, because that is how
    /// the money is counted everywhere else - a session that starts at 01:00 belongs to the
    /// night before, whoever happened to be on duty.
    ///
    /// Never throws. A summary that cannot be emailed must not stop an operator going home.
    /// </summary>
    private async Task SendDaySummaryAsync(Shift shift)
    {
        try
        {
            var (dayStart, dayEnd) = IndiaTime.BusinessDayRangeFor(shift.LogoutTime ?? DateTimeOffset.UtcNow);
            var businessDay = IndiaTime.BusinessDayOf(shift.LogoutTime ?? DateTimeOffset.UtcNow);
            var branchName = await _db.Branches.Where(b => b.Id == shift.BranchId)
                .Select(b => b.Name).FirstOrDefaultAsync() ?? "Unknown branch";

            var payments = await _db.Payments.AsNoTracking()
                .Where(p => p.BranchId == shift.BranchId && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
                .ToListAsync();

            var sessions = await _db.Sessions.AsNoTracking()
                .CountAsync(s => s.BranchId == shift.BranchId && s.StartTime >= dayStart && s.StartTime < dayEnd);

            var outages = await _db.DowntimeEvents.AsNoTracking()
                .Where(d => d.BranchId == shift.BranchId && d.StartedAt >= dayStart && d.StartedAt < dayEnd)
                .ToListAsync();

            var total = payments.Sum(p => p.TotalAmount);

            var rows = new List<(string, string)>
            {
                ("Branch", branchName),
                ("Day", $"{businessDay:dd MMM yyyy}"),
                ("Counted from", "6 in the morning to 6 the next morning"),
                ("Shop closed at", IndiaTime.Format(shift.LogoutTime ?? DateTimeOffset.UtcNow)),
                ("", ""),
                ("Total money taken", $"Rs {total:0.00}"),
                ("  Cash", $"Rs {payments.Sum(p => p.CashAmount):0.00}"),
                ("  Online or UPI", $"Rs {payments.Sum(p => p.OnlineAmount):0.00}"),
                ("  Paid from wallets", $"Rs {payments.Sum(p => p.WalletAmount):0.00}"),
                ("", ""),
                ("Customers who played", sessions.ToString()),
                ("Bills taken", payments.Count.ToString()),
                ("", ""),
                ("Problems today", outages.Count == 0
                    ? "None - the system ran all day"
                    : $"{outages.Count} interruption{(outages.Count == 1 ? "" : "s")}"),
            };

            foreach (var outage in outages)
            {
                var what = outage.Kind == DowntimeKind.InternetOffline
                    ? "Internet was down"
                    : "System was off";
                rows.Add(($"  {what}",
                    $"from {IndiaTime.FormatTime(outage.StartedAt)}, for {AdminEmailTemplate.Describe(TimeSpan.FromSeconds(outage.DurationSeconds))}"));
            }

            await _adminNotifier.NotifyAsync(
                $"{branchName} - money taken on {businessDay:dd MMM yyyy} - Rs {total:0.00}",
                AdminEmailTemplate.Compose(
                    $"How {branchName} did today",
                    AdminEmailTemplate.Green,
                    $"The shop has closed for the day. {branchName} took Rs {total:0.00} from {sessions} customer{(sessions == 1 ? "" : "s")} on {businessDay:dd MMM yyyy}.",
                    rows,
                    headline: $"Rs {total:0.00}",
                    footnote: "The day runs from 6 in the morning to 6 the next morning, so late-night play counts towards the day it started on. You are getting this because the operator ticked \"last shift of the day\" when they finished."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the end-of-day summary for shift {ShiftId}.", shift.Id);
        }
    }

    public async Task LogoutAsync(Guid userId, string role, Guid? shiftId, bool closesTradingDay = false)
    {
        if (role == Roles.Operator && shiftId.HasValue)
        {
            // Close the operator's shift
            var shift = await _db.Shifts.FirstOrDefaultAsync(
                s => s.Id == shiftId.Value && s.OperatorId == userId && s.Status == ShiftStatus.Active);
            if (shift != null)
            {
                shift.LogoutTime = DateTimeOffset.UtcNow;
                shift.Status = ShiftStatus.Completed;
                shift.ClosedTradingDay = closesTradingDay;
            }

            // Update operator status
            var op = await _db.Operators.FindAsync(userId);
            if (op != null)
            {
                op.Status = OperatorStatus.LoggedOut;
                op.IsOnline = false;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Operator shift ended: {UserId}, shift: {ShiftId}, closed the day: {ClosedDay}",
                userId, shiftId, closesTradingDay);

            // Sent after the save, so the figures in it are the ones on record. Only on the
            // last shift of the day: a summary after every shift would be partial, and with
            // four branches and several shifts each it would be ignored within a week.
            if (closesTradingDay && shift != null)
            {
                await SendDaySummaryAsync(shift);
            }
        }

        // Fetch user name for audit
        string userName = "Unknown";
        if (role == Roles.SuperAdmin)
        {
            var user = await _db.Users.FindAsync(userId);
            userName = user?.FullName ?? "Admin";
        }
        else
        {
            var op = await _db.Operators.FindAsync(userId);
            userName = op?.FullName ?? "Operator";
        }

        await _audit.LogAsync(new AuditEntry
        {
            UserId = role == Roles.SuperAdmin ? userId : null,
            OperatorId = role == Roles.Operator ? userId : null,
            UserRole = role,
            UserName = userName,
            Action = AuditActions.Logout,
            Details = new { shiftId },
        });

        // Revoke tokens globally for this user (hardens against stale token reuse)
        await _tokenRevocation.RevokeUserTokensAsync(userId, TimeSpan.FromDays(7));
    }

    /// <summary>
    /// SOP §11: Force Logout — Super Admin forcibly logs out an operator.
    /// Instantly revoke access, terminate session, block future login.
    /// Maps from: auth.service.js forceLogout()
    /// </summary>
    public async Task<ForceLogoutResponseDto> ForceLogoutAsync(Guid adminId, Guid operatorId)
    {
        var op = await _db.Operators
            .Include(o => o.Branch)
            .FirstOrDefaultAsync(o => o.Id == operatorId);
        if (op == null)
            throw new NotFoundException("Operator not found", "OPERATOR_NOT_FOUND");

        // Close ALL active shifts for this operator
        var activeShifts = await _db.Shifts
            .Where(s => s.OperatorId == operatorId && s.Status == ShiftStatus.Active)
            .ToListAsync();
        foreach (var shift in activeShifts)
        {
            shift.LogoutTime = DateTimeOffset.UtcNow;
            shift.Status = ShiftStatus.ForceClosed;
        }

        // A cash register must never outlive the shift that opened it. Closing the shift and
        // leaving the drawer open strands it: no shift owns it, so nobody can count it, and it
        // is still "open" so the next operator's End Shift picks up the previous operator's
        // takings as though they were their own. Their count then cannot balance, through no
        // fault of theirs.
        //
        // Closed as NOT counted, deliberately. Nobody counted this cash - the shift was ended
        // administratively, with no one at the drawer. Writing in a figure would invent a count
        // that never happened; leaving it empty keeps the money visibly unreconciled, which is
        // the truth and the thing somebody should follow up.
        //
        // Matched on the operator rather than only on the shifts closed just now, so a drawer
        // already stranded by an earlier force-logout is cleared up by the next one.
        var strandedRegisters = await _db.CashRegisters
            .Where(r => r.OperatorId == operatorId && r.Status == CashRegisterStatus.Open)
            .ToListAsync();

        foreach (var register in strandedRegisters)
        {
            register.Status = CashRegisterStatus.Closed;
            register.ClosedAt = DateTimeOffset.UtcNow;
            register.MismatchReason = string.IsNullOrWhiteSpace(register.MismatchReason)
                ? "Drawer was never counted - the shift was ended by an admin, with nobody at the till."
                : register.MismatchReason;
        }

        if (strandedRegisters.Count > 0)
        {
            _logger.LogWarning(
                "Force logout of {Operator} closed {Count} cash register(s) that were never counted, " +
                "holding Rs {Amount} between them.",
                op.FullName, strandedRegisters.Count, strandedRegisters.Sum(r => r.ExpectedDrawerCash));
        }

        // Set operator status to logged_out
        op.Status = OperatorStatus.LoggedOut;
        op.IsOnline = false;

        await _db.SaveChangesAsync();

        // Get admin name for audit
        var admin = await _db.Users.FindAsync(adminId);
        var adminName = admin?.FullName ?? "Admin";

        await _audit.LogAsync(new AuditEntry
        {
            UserId = adminId,
            UserRole = Roles.SuperAdmin,
            UserName = adminName,
            Action = AuditActions.ForcedLogout,
            TargetType = "operator",
            TargetId = operatorId,
            BranchId = op.BranchId,
            BranchName = op.Branch?.Name,
            Details = new { operatorName = op.FullName, reason = "Forced logout by Super Admin" },
        });

        _logger.LogWarning("FORCE LOGOUT: {Operator} by {Admin}", op.FullName, adminName);

        // Force revoke all existing tokens for this operator
        await _tokenRevocation.RevokeUserTokensAsync(operatorId, TimeSpan.FromDays(7));

        return new ForceLogoutResponseDto { Success = true, Operator = op.FullName };
    }

    /// <summary>
    /// Refresh access token — re-verify user is still active before issuing.
    /// Maps from: auth.service.js refreshAccessToken()
    /// </summary>
    public async Task<TokenResponseDto> RefreshAccessTokenAsync(string refreshToken)
    {
        var principal = _jwt.ValidateRefreshToken(refreshToken);
        if (principal == null)
            throw new AuthenticationException("Invalid refresh token", "REFRESH_INVALID");

        var id = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
        var role = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value;

        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(role))
            throw new AuthenticationException("Invalid refresh token", "REFRESH_INVALID");

        var userId = Guid.Parse(id);

        // Re-verify user still exists and is active
        if (role == Roles.SuperAdmin)
        {
            var active = await _db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active);
            if (!active) throw new AuthorizationException("Account is no longer active", "ACCOUNT_INACTIVE");
        }
        else if (role == Roles.Operator)
        {
            var active = await _db.Operators.AnyAsync(o => o.Id == userId && o.Status == OperatorStatus.Active);
            if (!active) throw new AuthorizationException("Account is no longer active", "ACCOUNT_INACTIVE");
        }

        // Filter out protocol claims (exp, iat, nbf, iss, aud, jti) to prevent duplicates and validation failures
        var protocolClaims = new System.Collections.Generic.HashSet<string>(new[] { 
            "exp", "iat", "nbf", "iss", "aud", "jti"
        });

        var claims = new Dictionary<string, string>();
        foreach (var claim in principal.Claims)
        {
            if (!protocolClaims.Contains(claim.Type) && !claims.ContainsKey(claim.Type))
                claims[claim.Type] = claim.Value;
        }

        return new TokenResponseDto { AccessToken = _jwt.GenerateAccessToken(claims) };
    }

    /// <summary>
    /// SOP §19: Get current user profile with permissions.
    /// Returns full dashboard permission map for frontend rendering.
    /// Maps from: auth.service.js getCurrentUser()
    /// </summary>
    public async Task<UserProfileDto> GetCurrentUserAsync(Guid userId, string role)
    {
        if (role == Roles.SuperAdmin || role == Roles.Admin)
        {
            var user = await _db.Users.FindAsync(userId);
            if (user == null) throw new NotFoundException("User not found", "USER_NOT_FOUND");

            return new UserProfileDto
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role,
                DashboardPermissions = user.DashboardPermissions != null 
                    ? JsonSerializer.Deserialize<object>(user.DashboardPermissions) 
                    : null,
                Status = user.Status.ToString().ToLowerInvariant(),
                LastLogin = user.LastLogin,
            };
        }

        if (role == Roles.Operator)
        {
            var op = await _db.Operators
                .Include(o => o.Branch)
                .FirstOrDefaultAsync(o => o.Id == userId);
            if (op == null) throw new NotFoundException("Operator not found", "OPERATOR_NOT_FOUND");

            // Get active shift
            var activeShift = await _db.Shifts
                .Where(s => s.OperatorId == userId && s.Status == ShiftStatus.Active)
                .OrderByDescending(s => s.LoginTime)
                .Select(s => new ActiveShiftDto { Id = s.Id, LoginTime = s.LoginTime })
                .FirstOrDefaultAsync();

            return new UserProfileDto
            {
                Id = op.Id,
                FullName = op.FullName,
                Username = op.Username,
                Role = Roles.Operator,
                BranchId = op.BranchId,
                BranchName = op.Branch?.Name,
                DashboardPermissions = op.DashboardPermissions != null
                    ? JsonSerializer.Deserialize<object>(op.DashboardPermissions)
                    : null,
                Status = op.Status.ToString().ToLowerInvariant(),
                LastLogin = op.LastLogin,
                ActiveShift = activeShift,
            };
        }

        throw new AppException("Invalid role", System.Net.HttpStatusCode.BadRequest, "INVALID_ROLE");
    }

    /// <summary>SOP §6.3 Step 2: Get active branches for login screen</summary>
    public async Task<IEnumerable<BranchListItemDto>> GetActiveBranchesAsync()
    {
        return await _db.Branches
            .Where(b => b.Status == BranchStatus.Active)
            .OrderBy(b => b.Name)
            .Select(b => new BranchListItemDto
            {
                Id = b.Id,
                Name = b.Name,
                Address = b.Address,
                Status = b.Status.ToString().ToLowerInvariant(),
                OpeningTime = b.OpeningTime.ToString("HH:mm"),
                ClosingTime = b.ClosingTime.ToString("HH:mm"),
            })
            .ToListAsync();
    }

    /// <summary>SOP §6.3 Step 3: Get operators for a branch (for operator selection screen)</summary>
    public async Task<IEnumerable<OperatorListItemDto>> GetBranchOperatorsAsync(Guid branchId)
    {
        return await _db.Operators
            .Where(o => o.BranchId == branchId && o.Status != OperatorStatus.Disabled)
            .OrderBy(o => o.FullName)
            .Select(o => new OperatorListItemDto
            {
                Id = o.Id,
                FullName = o.FullName,
                Username = o.Username,
                Status = o.Status.ToString().ToLowerInvariant(),
            })
            .ToListAsync();
    }

    /// <summary>Verify if admin password is valid</summary>
    public async Task<bool> VerifyAdminPasswordAsync(string password)
    {
        var admin = await _db.Users.FirstOrDefaultAsync(u => u.Role == Roles.SuperAdmin);
        if (admin == null) return false;
        return BCryptNet.Verify(password, admin.PasswordHash);
    }

    /// <summary>
    /// Generate a 30-day emergency offline JWT.
    /// The token is signed with the same access secret so the existing JWT middleware validates it.
    /// A "token_type" claim of "emergency_offline" allows the client to distinguish it from regular tokens.
    /// </summary>
    public async Task<string> GenerateEmergencyTokenAsync(Guid userId, string role, string? branchId, string? dashboardPermissions)
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = userId.ToString(),
            [ClaimTypes.Role] = role,
            ["token_type"] = "emergency_offline",
        };

        if (!string.IsNullOrEmpty(branchId))
            claims["branchId"] = branchId;

        if (!string.IsNullOrEmpty(dashboardPermissions))
            claims["dashboardPermissions"] = dashboardPermissions;

        // Resolve the user's display name for the audit log
        string userName = "Unknown";
        if (role == Roles.Operator)
        {
            var op = await _db.Operators.FindAsync(userId);
            if (op != null)
            {
                claims[ClaimTypes.Name] = op.FullName;
                userName = op.FullName;
            }
        }
        else
        {
            var user = await _db.Users.FindAsync(userId);
            if (user != null)
            {
                claims[ClaimTypes.Name] = user.FullName;
                userName = user.FullName;
            }
        }

        await _audit.LogAsync(new AuditEntry
        {
            UserId = role != Roles.Operator ? userId : null,
            OperatorId = role == Roles.Operator ? userId : null,
            UserRole = role,
            UserName = userName,
            Action = "emergency_token_generated",
            BranchId = !string.IsNullOrEmpty(branchId) && Guid.TryParse(branchId, out var bid) ? bid : null,
            Details = new { tokenType = "emergency_offline", expiryHours = 720 },
        });

        return _jwt.GenerateEmergencyToken(claims);
    }

    public async Task<CheckSetupResponseDto> CheckSetupStatusAsync()
    {
        var hasSuperAdmin = await _db.Users.AnyAsync(u => u.Role == Roles.SuperAdmin);
        var hasAdmin = await _db.Users.AnyAsync(u => u.Role == Roles.Admin);
        var hasOperator = await _db.Operators.AnyAsync();

        return new CheckSetupResponseDto
        {
            NeedsSuperAdminSetup = !hasSuperAdmin,
            NeedsAdminSetup = !hasAdmin,
            NeedsOperatorSetup = !hasOperator
        };
    }

    public async Task<LoginResponseDto> SetupMasterAccountAsync(SetupMasterDto dto)
    {
        var hasSuperAdmin = await _db.Users.AnyAsync(u => u.Role == Roles.SuperAdmin);
        if (hasSuperAdmin) throw new AuthorizationException("Master account already exists.", "SETUP_LOCKED");

        var adminHash = BCryptNet.HashPassword(dto.Password);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            FullName = dto.FullName,
            Role = Roles.SuperAdmin,
            Status = UserStatus.Active,
            PasswordHash = adminHash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return await LoginAdminAsync(new AdminLoginDto { Email = dto.Email, Password = dto.Password });
    }

    public async Task<LoginResponseDto> SetupOperatorAccountAsync(SetupOperatorDto dto)
    {
        var hasOperator = await _db.Operators.AnyAsync();
        if (hasOperator) throw new AuthorizationException("An operator already exists. Use the dashboard to create more.", "SETUP_LOCKED");

        var opHash = BCryptNet.HashPassword(dto.Password);
        var op = new Operator
        {
            Id = Guid.NewGuid(),
            FullName = dto.FullName,
            Username = dto.Username.Trim().ToLowerInvariant(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = opHash,
            BranchId = dto.BranchId,
            DashboardPermissions = "{}",
            Status = OperatorStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _db.Operators.Add(op);
        await _db.SaveChangesAsync();

        return await LoginOperatorAsync(new OperatorLoginDto { BranchId = dto.BranchId, Username = dto.Username, Password = dto.Password });
    }

    private static bool IsLocked(DateTimeOffset? lockedUntil) => lockedUntil.HasValue && lockedUntil.Value > DateTimeOffset.UtcNow;

    private static string LockoutMessage(DateTimeOffset lockedUntil)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockedUntil - DateTimeOffset.UtcNow).TotalMinutes));
        return $"Too many failed attempts. Try again in {minutes} minute(s), or use 'Forgot password' to reset it now.";
    }

    /// <summary>Never throws — a reset email that fails to send must not surface as a login-endpoint 500.</summary>
    private async Task SendPasswordResetEmailAsync(string? email, string targetName, string token, bool isLockout = false)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        try
        {
            var resetLink = _appUrls.BuildResetPasswordLink(email, token);
            var htmlBody = isLockout
                ? PasswordResetEmailTemplate.ComposeForLockout(targetName, resetLink)
                : PasswordResetEmailTemplate.Compose(targetName, resetLink);
            await _emailService.SendEmailAsync(email, PasswordResetEmailTemplate.Subject, htmlBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send the password-reset email to {Email}.", email);
        }
    }

    public async Task InitiatePasswordResetAsync(string email, string? accountType = null)
    {
        email = email.Trim().ToLowerInvariant();
        // Same email can belong to both a Member and a staff (User/Operator) account.
        // Scope the lookup to whichever screen the request came from so the reset never
        // lands on the wrong account type.
        var user = accountType == "member" ? null : await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        var op = accountType == "member" ? null : await _db.Operators.FirstOrDefaultAsync(o => o.Email == email);
        // Members can end up with duplicate rows sharing an email (e.g. abandoned re-registrations).
        // Prefer the active one so a reset never lands on a stale/suspended duplicate instead of
        // the account the person is actually trying to log into.
        var member = accountType == "staff" ? null : await _db.Members
            .Where(m => m.Email == email)
            .OrderByDescending(m => m.Status == MemberStatus.Active)
            .ThenByDescending(m => m.UpdatedAt)
            .FirstOrDefaultAsync();

        if (user == null && op == null && member == null) return; // Silent fail for security

        var token = Guid.NewGuid().ToString("N");
        var expiry = DateTimeOffset.UtcNow.AddHours(1);

        string targetName = "";
        if (user != null)
        {
            user.ResetToken = token;
            user.ResetTokenExpiry = expiry;
            targetName = user.FullName;
        }
        else if (op != null)
        {
            op.ResetToken = token;
            op.ResetTokenExpiry = expiry;
            targetName = op.FullName;
        }
        else if (member != null)
        {
            member.ResetToken = token;
            member.ResetTokenExpiry = expiry;
            targetName = member.FullName;
        }

        await _db.SaveChangesAsync();

        await SendPasswordResetEmailAsync(email, targetName, token);
    }

    public async Task<IEnumerable<AvailableAdminDto>> GetAvailableAdminsForSwitchAsync()
    {
        var activeAdmins = await _db.Users
            .Where(u => (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin) && u.Status == UserStatus.Active && !string.IsNullOrEmpty(u.AccessPin))
            .Select(u => new AvailableAdminDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Type = "Admin",
                PinLength = u.AccessPin!.Length
            })
            .ToListAsync();

        var activeOps = await _db.Operators
            .Where(o => o.IsGlobalAdmin && o.Status == OperatorStatus.Active && !string.IsNullOrEmpty(o.AccessPin))
            .Select(o => new AvailableAdminDto
            {
                Id = o.Id,
                FullName = o.FullName,
                Type = "Operator",
                PinLength = o.AccessPin!.Length
            })
            .ToListAsync();

        return activeAdmins.Concat(activeOps).OrderBy(a => a.FullName);
    }

    public async Task<LoginResponseDto> AdminSwitchInAsync(AdminSwitchInDto dto)
    {
        Guid adminId;
        string adminFullName;
        string? adminDashboardPermissions;
        UserStatus adminStatus;

        // 1. Try to find an Admin or SuperAdmin in the Users table
        var adminUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.AdminId && (u.Role == Roles.Admin || u.Role == Roles.SuperAdmin));
        if (adminUser != null)
        {
            if (adminUser.AccessPin != dto.AccessPin)
                throw new AuthenticationException("Invalid Admin PIN.", "INVALID_PIN");

            adminId = adminUser.Id;
            adminFullName = adminUser.FullName;
            adminDashboardPermissions = adminUser.DashboardPermissions;
            adminStatus = adminUser.Status;
        }
        else
        {
            var adminOp = await _db.Operators.FirstOrDefaultAsync(o => o.Id == dto.AdminId && o.IsGlobalAdmin);
            if (adminOp != null)
            {
                if (adminOp.AccessPin != dto.AccessPin)
                    throw new AuthenticationException("Invalid Admin PIN.", "INVALID_PIN");

                adminId = adminOp.Id;
                adminFullName = adminOp.FullName;
                adminDashboardPermissions = adminOp.DashboardPermissions;
                adminStatus = adminOp.Status == OperatorStatus.Active ? UserStatus.Active : UserStatus.Disabled;
            }
            else
            {
                throw new AuthenticationException("Admin not found.", "INVALID_ADMIN");
            }
        }

        if (adminStatus != UserStatus.Active)
            throw new AuthorizationException("Admin account is inactive.", "ACCOUNT_INACTIVE");

        var shift = await _db.Shifts.Include(s => s.Operator).ThenInclude(o => o.Branch).FirstOrDefaultAsync(s => s.Id == dto.ShiftId);
        if (shift == null || shift.Status != ShiftStatus.Active)
            throw new AuthorizationException("Active operator shift not found for switch.", "INVALID_SHIFT");

        // Generate temporary token for the Admin Switch
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = adminId.ToString(),
            [ClaimTypes.Role] = Roles.Admin,
            [ClaimTypes.Name] = adminFullName,
            ["originalOperatorId"] = shift.OperatorId.ToString(),
            ["shiftId"] = shift.Id.ToString(),
            ["branchId"] = shift.BranchId.ToString(),
            ["isSwitchedAdmin"] = "true"
        };

        if (!string.IsNullOrEmpty(adminDashboardPermissions))
        {
            claims["dashboardPermissions"] = adminDashboardPermissions;
        }

        var accessToken = _jwt.GenerateAccessToken(claims);

        await _audit.LogAsync(new AuditEntry
        {
            UserId = adminId,
            UserRole = Roles.Admin,
            UserName = adminFullName,
            OperatorId = shift.OperatorId, // associate with the operator
            Action = AuditActions.AdminSwitchIn,
            Details = new { shiftId = shift.Id, operatorName = shift.Operator.FullName, branchId = shift.BranchId }
        });

        return new LoginResponseDto
        {
            User = new UserProfileDto
            {
                Id = adminId,
                FullName = adminFullName,
                Email = adminFullName, // Set email to full name as fallback
                Role = Roles.Admin,
                BranchId = shift.BranchId,
                BranchName = shift.Operator.Branch.Name,
                ShiftId = shift.Id,
                DashboardPermissions = adminDashboardPermissions != null ? JsonSerializer.Deserialize<object>(adminDashboardPermissions) : null,
                Status = adminStatus.ToString().ToLowerInvariant(),
                LastLogin = DateTimeOffset.UtcNow
            },
            AccessToken = accessToken,
            RefreshToken = accessToken // We don't refresh this, it's ephemeral
        };
    }

    public async Task AdminSwitchOutAsync(Guid adminId, Guid shiftId)
    {
        var admin = await _db.Users.FindAsync(adminId);
        if (admin != null)
        {
            await _audit.LogAsync(new AuditEntry
            {
                UserId = admin.Id,
                UserRole = Roles.Admin,
                UserName = admin.FullName,
                Action = AuditActions.AdminSwitchOut,
                Details = new { shiftId }
            });
        }
    }

    public async Task CompletePasswordResetAsync(ResetPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.ResetToken == dto.Token && u.ResetTokenExpiry > DateTimeOffset.UtcNow);
        var op = await _db.Operators.FirstOrDefaultAsync(o => o.Email == email && o.ResetToken == dto.Token && o.ResetTokenExpiry > DateTimeOffset.UtcNow);
        var member = await _db.Members.FirstOrDefaultAsync(m => m.Email == email && m.ResetToken == dto.Token && m.ResetTokenExpiry > DateTimeOffset.UtcNow);

        if (user == null && op == null && member == null) throw new AuthorizationException("Invalid or expired reset token.");

        var newHash = BCryptNet.HashPassword(dto.NewPassword);
        if (user != null)
        {
            user.PasswordHash = newHash;
            user.ResetToken = null;
            user.ResetTokenExpiry = null;
            user.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else if (op != null)
        {
            op.PasswordHash = newHash;
            op.ResetToken = null;
            op.ResetTokenExpiry = null;
            op.UpdatedAt = DateTimeOffset.UtcNow;
        }
        else if (member != null)
        {
            member.PasswordHash = newHash;
            member.ResetToken = null;
            member.ResetTokenExpiry = null;
            member.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync();

        if (user != null)
        {
            await _audit.LogAsync(new AuditEntry
            {
                UserId = user.Id,
                UserRole = user.Role,
                UserName = user.FullName,
                Action = AuditActions.PasswordReset,
                TargetType = "user",
                TargetId = user.Id,
                Details = new { status = "success", resetAt = DateTimeOffset.UtcNow },
            });
        }
        else if (op != null)
        {
            await _audit.LogAsync(new AuditEntry
            {
                OperatorId = op.Id,
                UserRole = op.IsGlobalAdmin ? Roles.Admin : Roles.Operator,
                UserName = op.FullName,
                Action = AuditActions.PasswordReset,
                TargetType = "operator",
                TargetId = op.Id,
                Details = new { status = "success", resetAt = DateTimeOffset.UtcNow },
            });
        }
        else if (member != null)
        {
            await _audit.LogAsync(new AuditEntry
            {
                UserRole = "Member",
                UserName = member.FullName,
                Action = AuditActions.PasswordReset,
                TargetType = "member",
                TargetId = member.Id,
                Details = new { status = "success", resetAt = DateTimeOffset.UtcNow },
            });
        }
    }

    public async Task ChangeCredentialsAsync(Guid targetUserId, ChangeCredentialsDto dto)
    {
        var user = await _db.Users.FindAsync(targetUserId);
        if (user != null)
        {
            if (!string.IsNullOrEmpty(dto.NewEmail)) user.Email = dto.NewEmail.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(dto.NewPassword)) user.PasswordHash = BCryptNet.HashPassword(dto.NewPassword);
        }
        else
        {
            var op = await _db.Operators.FindAsync(targetUserId);
            if (op != null)
            {
                if (!string.IsNullOrEmpty(dto.NewEmail)) op.Email = dto.NewEmail.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(dto.NewUsername)) op.Username = dto.NewUsername.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(dto.NewPassword)) op.PasswordHash = BCryptNet.HashPassword(dto.NewPassword);
            }
            else
            {
                throw new NotFoundException("Target user or operator not found.");
            }
        }
        await _db.SaveChangesAsync();
    }
}
