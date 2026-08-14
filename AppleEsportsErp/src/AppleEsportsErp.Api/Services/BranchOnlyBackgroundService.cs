using AppleEsportsErp.Infrastructure.Configuration;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// A background job that must only ever run where the shop actually is.
///
/// Head Office holds a synced copy of every session, shift and till in the company. To the
/// code, those rows are indistinguishable from a branch's own - same tables, same states, same
/// timestamps. So every background job that acts on trading data was, at Head Office, quietly
/// pointed at four other people's shops.
///
/// The consequences were not subtle. The fixed-duration monitor auto-stops any Active session
/// whose time is up: at Head Office that is every branch's sessions, stopped and billed a
/// second time, in a database no operator can see and no customer is standing in. The trading
/// day closer closes shifts and cash registers and emails the owner about the difference - the
/// difference between a branch's real till and a copy of it that Head Office had already
/// closed by itself. Two sets of books, disagreeing, with nothing on either screen saying why.
///
/// This did not show up sooner only because SessionService refused to stop a session at Head
/// Office at all, so the monitors threw on every pass and logged it as an error. That refusal
/// is being lifted so Head Office can control its branches properly, which turns a job that
/// was failing loudly into one that would succeed silently and wrongly. Hence this class,
/// added first.
///
/// Head Office learns what happened the one correct way: the branch does it, and tells it.
/// </summary>
public abstract class BranchOnlyBackgroundService : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    protected BranchOnlyBackgroundService(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.IsHeadOffice())
        {
            // Said plainly and once, because "why is this not running?" is a reasonable
            // question to ask of a service that has deliberately stopped itself.
            _logger.LogInformation(
                "{Service} does not run at Head Office. It acts on live trading, and the only " +
                "trading here belongs to the branches - it would be acting on their records " +
                "behind their backs. The branches run this themselves and report the result.",
                GetType().Name);
            return;
        }

        await RunAsync(stoppingToken);
    }

    /// <summary>The actual work, reached only on a branch.</summary>
    protected abstract Task RunAsync(CancellationToken stoppingToken);
}
