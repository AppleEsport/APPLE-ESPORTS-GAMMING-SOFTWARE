using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Sync;
using AppleEsportsErp.Application.Services;
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
        var branchId = await db.Branches.AsNoTracking().Select(b => b.Id).FirstOrDefaultAsync(ct);
        if (branchId == Guid.Empty) return;

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

        if (!response.IsSuccessStatusCode && DateTimeOffset.UtcNow - _lastComplaint > ComplainAtMost)
        {
            _lastComplaint = DateTimeOffset.UtcNow;
            _logger.LogWarning("Head Office refused this branch's status: {Status}.", response.StatusCode);
        }
    }
}
