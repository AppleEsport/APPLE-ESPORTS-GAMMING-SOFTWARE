using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Controllers;
using AppleEsportsErp.Application.Constants;
using AppleEsportsErp.Application.Interfaces;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Infrastructure.Configuration;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Lets Head Office drive any branch, by asking rather than by writing.
///
/// The requirement is simple and was asked for plainly: from Head Office, do anything - stop a
/// session at any shop, start one, take a PC out of service. What made it hard was that the
/// obvious implementation is wrong in a way that looks right for about three seconds.
///
/// Head Office holds a synced copy of every branch's rows. Writing to that copy is easy, and
/// it updates the Head Office screen immediately, so it demos perfectly. It also does nothing
/// whatsoever. The shop's own database is a different machine in a different city; it has not
/// been told, so the counter still shows the PC as busy, the operator still cannot bill it, and
/// three seconds later that shop's next heartbeat reports the truth and Head Office's screen
/// flips back. That is the "sometimes it works, sometimes it doesn't" everybody has been
/// chasing, and it is not a race condition to be tightened up - the write was never real.
///
/// So Head Office states an intent and the branch carries it out, through the identical code
/// path an operator's own click uses. The session is stopped by the shop that has the customer,
/// the cash and the till. It is billed normally, it lands in that shift's takings, and it syncs
/// back up on its own. Head Office and the branch agree afterwards because only one of them
/// ever acted - and the one that acted is the one that could.
///
/// The cost is that a command is not instant: it is queued, collected on the next beat, and
/// confirmed. In practice that is a few seconds, and it is honest about them. An instant lie
/// was what we had before.
/// </summary>
public interface IRemoteBranchControl
{
    /// <summary>
    /// True when this instance is Head Office, so an action aimed at a branch has to travel.
    /// False at a branch, where the shop is right here and acts directly.
    /// </summary>
    bool MustTravel { get; }

    /// <summary>
    /// Puts a command in the branch's queue and returns what to tell whoever pressed the
    /// button. Refuses politely rather than throwing when the branch is not reachable at all,
    /// because "this shop has never reported in" is a useful thing to be told immediately
    /// instead of after a command sits unclaimed for a day.
    /// </summary>
    Task<RemoteCommandReceipt> SendAsync(
        Guid branchId, string commandType, object payload, Guid requestedByUserId, CancellationToken ct);
}

/// <summary>What Head Office can tell the person who pressed the button, right now.</summary>
public record RemoteCommandReceipt(Guid CommandId, string Message, bool BranchIsReporting);

public class RemoteBranchControl : IRemoteBranchControl
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IAuditService _audit;
    private readonly ILogger<RemoteBranchControl> _logger;

    public RemoteBranchControl(
        AppDbContext db, IConfiguration configuration, IAuditService audit, ILogger<RemoteBranchControl> logger)
    {
        _db = db;
        _configuration = configuration;
        _audit = audit;
        _logger = logger;
    }

    public bool MustTravel => _configuration.IsHeadOffice();

    public async Task<RemoteCommandReceipt> SendAsync(
        Guid branchId, string commandType, object payload, Guid requestedByUserId, CancellationToken ct)
    {
        var command = new BranchCommand
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            CommandType = commandType,
            Payload = JsonSerializer.Serialize(payload),
            Status = BranchCommandStatus.Pending,
            RequestedByUserId = requestedByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Add(command);
        await _db.SaveChangesAsync(ct);

        // Whether the shop is actually listening, checked against the same silence window the
        // overview screen uses. A command for a branch whose counter PC is switched off is not
        // an error - it will be collected whenever that PC comes back - but saying so now is
        // the difference between "nothing happened" and "nothing happened yet".
        var beat = await _db.Set<BranchHeartbeat>().AsNoTracking()
            .FirstOrDefaultAsync(h => h.BranchId == branchId, ct);

        var reporting = beat is not null
            && DateTimeOffset.UtcNow - beat.LastSeenAt < BranchHeartbeatController.SilentAfter;

        _logger.LogInformation(
            "Queued {CommandType} for branch {BranchId}, requested by {UserId}. Branch reporting: {Reporting}.",
            commandType, branchId, requestedByUserId, reporting);

        // The half of this story that belongs to the person who asked, not the branch that
        // carried it out. Once the branch actually runs the command it logs its own entry the
        // normal way - StartSessionAsync's SessionStart, StopSessionAsync's SessionStop, and so
        // on - exactly as if it had happened at the counter, because it is the same code doing
        // it. Neither entry alone answers both questions a real incident needs answered: this
        // one says who decided and when; the branch's own says what actually happened and
        // whether it worked. Logged even for a branch that is not currently reporting - the
        // decision was still made the moment the button was pressed, whatever happens after.
        var requestedBy = await _db.Set<Domain.Entities.User>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == requestedByUserId, ct);

        var branchName = await _db.Branches.AsNoTracking()
            .Where(b => b.Id == branchId).Select(b => b.Name).FirstOrDefaultAsync(ct);

        await _audit.LogAsync(new AuditEntry
        {
            UserId = requestedByUserId,
            UserRole = requestedBy?.Role ?? "super_admin",
            UserName = requestedBy?.FullName ?? "Head Office",
            Action = AuditActions.RemoteCommandIssued,
            TargetType = "branch_command",
            TargetId = command.Id,
            BranchId = branchId,
            BranchName = branchName,
            Details = new { commandType, branchReporting = reporting, payload },
        });

        var message = reporting
            ? "Sent to the branch. It carries this out within a few seconds and reports back."
            : "Queued. This branch is not reporting at the moment, so it will be carried out as " +
              "soon as its counter PC is back online.";

        return new RemoteCommandReceipt(command.Id, message, reporting);
    }
}

/// <summary>
/// The command words, in one place so Head Office and the branch cannot drift apart on
/// spelling. A branch that meets a word it does not recognise says so and leaves the command
/// alone, so adding one here does not break shops running an older build.
/// </summary>
public static class BranchCommands
{
    public const string StopSession = "stop_session";
    public const string StartSession = "start_session";
    public const string SetPcState = "set_pc_state";
    public const string TransferSession = "transfer_session";

    /// <summary>
    /// Installs the exact version named, older or newer than what is currently running - a
    /// person at Head Office has already decided, so this bypasses the branch's own "only
    /// ever go forward" rule (desktop-client's UpdateService.CheckAsync), which exists for the
    /// unattended nightly check, not for an explicit instruction. See
    /// BranchHeartbeatService.RunInstallVersionAsync for why this is the one command capable
    /// of cutting off the very channel that carries it, if it goes wrong.
    /// </summary>
    public const string InstallVersion = "install_version";
}
