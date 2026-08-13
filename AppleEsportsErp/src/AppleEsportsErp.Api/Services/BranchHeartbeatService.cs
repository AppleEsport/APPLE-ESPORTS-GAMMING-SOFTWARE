using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Sync;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Infrastructure.Configuration;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Tells Head Office what this shop is doing, every thirty seconds.
///
/// The reason this exists rather than another dozen event types: everything anybody wanted to
/// see at Head Office had to be wired by hand into whichever service happened to change it, and
/// the list of what synced became whatever somebody remembered. Sessions were remembered. Bills
/// were remembered. Operator status was not, so a branch trading all evening showed its staff as
/// logged out. PC state was not, so Head Office displayed early August for four days running.
///
/// State is not history and does not want the same machinery. Only the newest beat matters, so
/// there is no queue and no retry: a missed one costs nothing because the next is thirty seconds
/// away. That is what makes it safe to send this often, and why nothing here can lose money.
///
/// Runs on branches only. Head Office is the one being told.
/// </summary>
public class BranchHeartbeatService : BackgroundService
{
    private readonly ILogger<BranchHeartbeatService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Three seconds, so Head Office is watching the shop rather than reading a report about it.
    ///
    /// Thirty was chosen to be frugal and was simply too slow to trust: an owner who starts a
    /// session and stares at an unchanged screen for half a minute concludes sync is broken, and
    /// checking by waiting is no way to run four branches. At three seconds the two screens
    /// agree while you are still looking at them.
    ///
    /// What makes this affordable is on the receiving side, not here: Head Office now writes only
    /// rows whose values actually changed. A quiet shop therefore costs one small row update per
    /// beat no matter how many PCs it has, instead of rewriting every PC twenty times a minute.
    ///
    /// The cost that remains is bandwidth - a few KB each way, so roughly 100 MB a day per
    /// branch. Nothing on a broadband line. Worth knowing if a branch ever runs on a phone
    /// tether for a day.
    /// </summary>
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Logged at most this often when the line is down. Without it a shop offline for a night
    /// writes a warning every thirty seconds — two and a half thousand lines saying the same
    /// thing, burying whatever else happened.
    /// </summary>
    private static readonly TimeSpan ComplainAtMost = TimeSpan.FromMinutes(15);
    private DateTimeOffset _lastComplaint = DateTimeOffset.MinValue;

    public BranchHeartbeatService(
        ILogger<BranchHeartbeatService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
    }

    private static string RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.IsHeadOffice())
        {
            _logger.LogInformation(
                "This instance is Head Office, so it reports its state to nobody. Branches report here.");
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BeatAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                if (DateTimeOffset.UtcNow - _lastComplaint > ComplainAtMost)
                {
                    _lastComplaint = DateTimeOffset.UtcNow;
                    _logger.LogWarning(
                        "Cannot reach Head Office to report this branch's status ({Reason}). " +
                        "The shop is unaffected; this is only reporting.",
                        ex.GetBaseException().Message);
                }
            }

            await Task.Delay(Every, stoppingToken);
        }
    }

    private async Task BeatAsync(CancellationToken ct)
    {
        var headOffice = _configuration["Sync:HeadOfficeUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(headOffice)) return;

        using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // One branch row, its own, once adopted. Before that there is nothing to report about.
        // Ordered, so a branch whose local database somehow holds more than one row reports
        // the same one every time. Unordered, PostgreSQL is free to return them in any order
        // and the shop could report itself as a different branch between beats.
        var branchId = await db.Branches.AsNoTracking()
            .OrderBy(b => b.Id).Select(b => b.Id).FirstOrDefaultAsync(ct);

        if (branchId == Guid.Empty) return;
        _branchId = branchId;

        var today = IndiaTime.BusinessDayOf(DateTimeOffset.UtcNow);
        var (dayStart, dayEnd) = IndiaTime.BusinessDayRange(today);

        // Who is actually standing at a counter, taken from open shifts rather than from the
        // operator's own status flag. A flag can be left behind by a crash; an open shift is
        // the thing the shop is actually working against.
        var onDuty = await db.Shifts.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Status == ShiftStatus.Active)
            .Join(db.Operators.AsNoTracking(), s => s.OperatorId, o => o.Id,
                  (s, o) => new OperatorOnDutyDto
                  {
                      OperatorId = o.Id,
                      FullName = o.FullName,
                      ShiftStartedAt = s.LoginTime,
                  })
            .ToListAsync(ct);

        var pcs = await db.Pcs.AsNoTracking()
            .Where(p => p.BranchId == branchId && !p.IsDeleted)
            .Select(p => new PcStateDto
            {
                PcId = p.Id,
                State = p.State.ToString().ToLowerInvariant(),
                CurrentSessionId = p.CurrentSessionId,
            })
            .ToListAsync(ct);

        var activeSessions = await db.Sessions.AsNoTracking()
            .CountAsync(s => s.BranchId == branchId && s.State == SessionState.Active, ct);

        // Null when nothing is open, and that is not the same as zero: an empty drawer and no
        // drawer at all mean different things to whoever is looking at this.
        var drawer = await db.CashRegisters.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.BusinessDay == today && r.Status != CashRegisterStatus.Closed)
            .OrderByDescending(r => r.OpenedAt)
            .Select(r => (decimal?)r.ExpectedDrawerCash)
            .FirstOrDefaultAsync(ct);

        var takings = await db.Payments.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.CreatedAt >= dayStart && p.CreatedAt < dayEnd)
            .SumAsync(p => (decimal?)p.TotalAmount, ct) ?? 0m;

        // How far behind sync is. Anything queued and undelivered is money Head Office cannot
        // see yet, so it belongs on the same screen as the takings rather than buried in a log.
        var undelivered = await db.SyncOutboxEntries.AsNoTracking()
            .CountAsync(e => e.SyncedAt == null, ct);

        var beat = new BranchHeartbeatDto
        {
            BranchId = branchId,
            Version = RunningVersion,
            MachineName = Environment.MachineName,
            ConfigVersion = _configVersion,
            BranchLocalTime = IndiaTime.Now,
            OperatorsOnDuty = onDuty,
            Pcs = pcs,
            ActiveSessions = activeSessions,
            DrawerExpected = drawer,
            TakingsToday = takings,
            UndeliveredRecords = undelivered,
        };

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);

        var response = await client.PostAsync(
            $"{headOffice}/api/branch-status",
            new StringContent(JsonSerializer.Serialize(beat), Encoding.UTF8, "application/json"),
            ct);

        if (!response.IsSuccessStatusCode)
        {
            if (DateTimeOffset.UtcNow - _lastComplaint > ComplainAtMost)
            {
                _lastComplaint = DateTimeOffset.UtcNow;
                _logger.LogWarning("Head Office refused this branch's status: {Status}.", response.StatusCode);
            }
            return;
        }

        await ApplyConfigFromReplyAsync(db, await response.Content.ReadAsStringAsync(ct), ct);
    }

    /// <summary>
    /// Takes whatever settings Head Office sent back and makes this branch match them.
    ///
    /// This is the direction that never existed. A super admin could grant an operator End of
    /// Day, watch the server save it, and the counter would never hear - every permission
    /// screen on the server was decoration, and an operator hired at Head Office could not log
    /// in at the shop they had been hired for.
    ///
    /// Head Office sends nothing at all when this branch is already correct, so the usual case
    /// is a few hundred bytes and this method does nothing.
    /// </summary>
    private async Task ApplyConfigFromReplyAsync(AppDbContext db, string body, CancellationToken ct)
    {
        BranchConfigDto? config;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return;
            if (!data.TryGetProperty("config", out var node) || node.ValueKind == JsonValueKind.Null) return;

            config = JsonSerializer.Deserialize<BranchConfigDto>(node.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read the settings Head Office sent ({Reason}).",
                ex.GetBaseException().Message);
            return;
        }

        if (config is null || config.Operators.Count == 0) return;

        var known = await db.Operators.ToDictionaryAsync(o => o.Id, ct);
        var changed = 0;

        foreach (var incoming in config.Operators)
        {
            if (!known.TryGetValue(incoming.Id, out var op))
            {
                // Somebody hired at Head Office who has never existed here. Created with the
                // same id, so everything they later do lines up on both sides rather than
                // arriving as a stranger.
                op = new Operator
                {
                    Id = incoming.Id,
                    BranchId = _branchId,
                    Status = OperatorStatus.LoggedOut,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Operators.Add(op);
                changed++;
            }

            op.FullName = incoming.FullName;
            op.Username = incoming.Username;
            op.Email = incoming.Email;
            op.PasswordHash = incoming.PasswordHash;
            op.MobileNumber = incoming.MobileNumber;
            op.AccessPin = incoming.AccessPin;
            op.IsGlobalAdmin = incoming.IsGlobalAdmin;
            op.DashboardPermissions = incoming.DashboardPermissions;
            op.UpdatedAt = DateTimeOffset.UtcNow;

            // Only the barred/not-barred decision comes down. Active and LoggedOut say whether
            // somebody is standing at this counter, which Head Office cannot know and must
            // never overwrite - doing so would sign out the operator halfway through a shift.
            if (incoming.IsBlocked)
            {
                if (op.Status is not (OperatorStatus.Suspended or OperatorStatus.Disabled))
                    op.Status = OperatorStatus.Suspended;
            }
            else if (op.Status is OperatorStatus.Suspended or OperatorStatus.Disabled)
            {
                op.Status = OperatorStatus.LoggedOut;   // unbarred; duty is decided here
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);

            // Recorded once, not every beat: the fingerprint now matches so Head Office stops
            // sending it. Worth a line in the log, because "the shop suddenly behaves
            // differently" should always have something to point at.
            _logger.LogInformation(
                "Settings updated from Head Office: {Count} operator(s), {New} new. Version {Version}.",
                config.Operators.Count, changed, config.Version);
        }

        _configVersion = config.Version;
    }

    /// <summary>
    /// The settings fingerprint this branch is running on, sent with every beat so Head Office
    /// can stay silent while it matches. Held in memory only - after a restart the branch
    /// simply asks once more and is told once more, which costs one message.
    /// </summary>
    private string? _configVersion;

    /// <summary>This branch's own id, remembered from the beat that was just built.</summary>
    private Guid _branchId;
}
