using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Infrastructure.Services;

/// <summary>
/// Works out who counts as an admin, and emails them.
///
/// Three sources, combined rather than chosen between, because relying on any single one is
/// how every admin email on the live system ended up going nowhere:
///
///   1. the receivers listed in system settings — empty on the live system
///   2. admin and super admin rows in `users` — where the owner's account actually is
///   3. operators flagged IsGlobalAdmin — none exist, which is what silently emptied the
///      member alerts
///
/// If the combined list is still empty that is said out loud, at Warning. An alert nobody
/// receives is worse than no alert at all: the shop believes it is being watched.
/// </summary>
public class AdminNotifier : IAdminNotifier
{
    private readonly AppDbContext _db;
    private readonly IEmailService _emailService;
    private readonly ILogger<AdminNotifier> _logger;

    public AdminNotifier(AppDbContext db, IEmailService emailService, ILogger<AdminNotifier> logger)
    {
        _db = db;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task NotifyAsync(string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        try
        {
            var recipients = await ResolveRecipientsAsync(cancellationToken);

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "No admin recipient for \"{Subject}\". Add an address under Settings, or give the " +
                    "owner's account the admin role, or nobody will be told when this happens.",
                    subject);
                return;
            }

            // One message, several addresses. SendEmailAsync splits on commas itself, and a
            // single send keeps the alert atomic - either everyone hears or the failure is
            // logged once, rather than half the admins being told.
            await _emailService.SendEmailAsync(string.Join(",", recipients), subject, htmlBody);

            _logger.LogInformation("Admin alert \"{Subject}\" sent to {Count} recipient(s).",
                subject, recipients.Count);
        }
        catch (Exception ex)
        {
            // Deliberately swallowed. This is called from inside closing a shift, taking
            // money and recording an outage; none of those may fail because the mail did.
            _logger.LogError(ex, "Could not send the admin alert \"{Subject}\".", subject);
        }
    }

    public async Task NotifyOperatorsAsync(string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        try
        {
            // Real addresses only. Operators created without one are given
            // "<username>@appleesports.local", which is not a mailbox anywhere - sending to it
            // just earns a bounce, and on some providers enough bounces cost you the ability
            // to send at all. So those are skipped rather than attempted.
            var operators = await _db.Operators.AsNoTracking()
                .Where(o => o.Status == OperatorStatus.Active && !o.Email.EndsWith(".local"))
                .Select(o => o.Email)
                .ToListAsync(cancellationToken);

            var admins = await ResolveRecipientsAsync(cancellationToken);

            var recipients = operators.Concat(admins)
                .Select(e => e.Trim())
                .Where(e => e.Length > 0 && e.Contains('@'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (recipients.Count == 0)
            {
                _logger.LogWarning(
                    "No operator or admin has a real email address, so \"{Subject}\" reached nobody.",
                    subject);
                return;
            }

            await _emailService.SendEmailAsync(string.Join(",", recipients), subject, htmlBody);

            _logger.LogInformation("Notified {Count} operator(s)/admin(s): \"{Subject}\".",
                recipients.Count, subject);
        }
        catch (Exception ex)
        {
            // Same reasoning as above: approving an update must not fail because the mail did.
            _logger.LogError(ex, "Could not tell the operators about \"{Subject}\".", subject);
        }
    }

    private async Task<List<string>> ResolveRecipientsAsync(CancellationToken ct)
    {
        var found = new List<string>();

        // 1. Whatever the owner has typed into Settings.
        var config = await _db.SystemConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConfigKey == "global_system_rules", ct);

        if (!string.IsNullOrWhiteSpace(config?.ConfigValue))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(config.ConfigValue);
                if (doc.RootElement.TryGetProperty("emailNotifications", out var node) &&
                    node.TryGetProperty("receivers", out var receivers) &&
                    receivers.GetString() is { Length: > 0 } list)
                {
                    found.AddRange(list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
            catch (Exception ex)
            {
                // Malformed settings must not cost us the other two sources.
                _logger.LogWarning(ex, "Could not read the notification receivers from settings.");
            }
        }

        // 2. The owner and any admins. This is where the super admin actually lives.
        found.AddRange(await _db.Users.AsNoTracking()
            .Where(u => (u.Role == Roles.SuperAdmin || u.Role == Roles.Admin) && u.Status == UserStatus.Active)
            .Select(u => u.Email)
            .ToListAsync(ct));

        // 3. Operators marked as global admins, for installations that use that flag.
        found.AddRange(await _db.Operators.AsNoTracking()
            .Where(o => o.IsGlobalAdmin && o.Status == OperatorStatus.Active)
            .Select(o => o.Email)
            .ToListAsync(ct));

        return found
            .Select(e => e.Trim())
            .Where(e => e.Length > 0 && e.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
