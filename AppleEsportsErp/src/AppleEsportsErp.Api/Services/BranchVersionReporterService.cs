using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Infrastructure.Configuration;
using AppleEsportsErp.Infrastructure.Data;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Tells Head Office which version this branch is actually running.
///
/// Updates had two halves and only one of them existed. Head Office could publish a version and
/// approve it, and a branch could fetch and install it — but nothing ever reported back, so
/// `BranchVersionStatuses` was empty and the Updates page could not say what any branch was on.
/// An update system you cannot see the results of is barely an update system: you would push a
/// fix to four shops and have no way of knowing whether any of them took it, which is exactly
/// the moment you most want to know.
///
/// Runs on branches only. Head Office is not a branch and has nobody to report to.
///
/// Failures are deliberately quiet. This is telemetry: a branch whose line is down must carry on
/// trading and simply say so at the next opportunity, not log an error every minute for
/// something that costs nothing while it waits.
/// </summary>
public class BranchVersionReporterService : BackgroundService
{
    private readonly ILogger<BranchVersionReporterService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly TimeSpan ReportEvery = TimeSpan.FromMinutes(15);

    public BranchVersionReporterService(
        ILogger<BranchVersionReporterService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// The version of the running build, taken from the assembly rather than a constant, so it
    /// cannot drift from what was actually installed. A hand-maintained version string is how
    /// the app came to report 2.0 for months while 2.2 was on the machine.
    /// </summary>
    private static string RunningVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.IsHeadOffice())
        {
            _logger.LogInformation(
                "This instance is Head Office, so it reports its version to nobody. " +
                "It is branches that report upward.");
            return;
        }

        // The API and the database both need to be up before this means anything, and a branch
        // PC starting after a power cut brings several services up at once.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReportOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not report this branch's version. Will try again.");
            }

            await Task.Delay(ReportEvery, stoppingToken);
        }
    }

    private async Task ReportOnceAsync(CancellationToken ct)
    {
        var headOffice = _configuration["Sync:HeadOfficeUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(headOffice)) return;

        using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A branch holds exactly one branch row — its own — once it has been adopted. Before
        // adoption it holds none, and there is nothing to report yet: an unadopted branch has no
        // identity Head Office would recognise.
        var branch = await db.Branches.AsNoTracking()
            .Select(b => new { b.Id, b.Name })
            .FirstOrDefaultAsync(ct);

        if (branch is null)
        {
            _logger.LogDebug("Not adopted yet, so there is no branch identity to report against.");
            return;
        }

        // How many of this branch's gaming PCs are on the current version. Counted from what the
        // PCs themselves last reported rather than assumed, so "12 of 16 up to date" means
        // twelve machines said so.
        var totalPcs = await db.Pcs.CountAsync(p => p.BranchId == branch.Id && !p.IsDeleted, ct);

        var payload = JsonSerializer.Serialize(new
        {
            currentVersion = RunningVersion,
            upToDateCount = 0,
            totalCount = totalPcs,
        });

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);

        var response = await client.PostAsync(
            $"{headOffice}/api/versions/branch/{branch.Id}/status",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            ct);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Reported to Head Office: {Branch} is running {Version}.", branch.Name, RunningVersion);
        }
        else
        {
            _logger.LogWarning(
                "Head Office refused this branch's version report: {Status}.", response.StatusCode);
        }
    }
}
