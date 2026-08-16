using AppleEsportsErp.Api.Controllers;
using AppleEsportsErp.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Periodically re-attempts whatever is sitting in the sync inbox unapplied.
///
/// A branch's own retry only fires when it resends a batch it never got a response for - a
/// dropped connection, not a stuck dependency. When a cash register's sync entry lands before
/// the shift it names, the branch was already told "delivered" and has no reason to ever send
/// that entry again; without this, it stays wrong until something else happens to touch the
/// same row and generate a fresh entry, which was the actual state of production before this
/// existed - a register open at 06:07 whose data never reached Head Office until it was closed
/// at 17:03, eleven hours later, purely because nothing else modified it in between.
///
/// Head Office only, and deliberately not folded into BranchOnlyBackgroundService's family -
/// that base class exists to keep branch-trading jobs off Head Office; this is the mirror
/// image, a Head-Office-only job with nothing to do at a branch, since a branch's own inbox is
/// never populated - it only ever sends, never receives.
/// </summary>
public class SyncInboxRetryService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SyncInboxRetryService> _logger;

    public SyncInboxRetryService(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger<SyncInboxRetryService> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.IsHeadOffice())
        {
            _logger.LogInformation(
                "SyncInboxRetryService does not run at a branch - a branch's own inbox is " +
                "never populated, so there is nothing here for it to retry.");
            return;
        }

        _logger.LogInformation("SyncInboxRetryService starting (every {Interval}).", Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var controller = ActivatorUtilities.CreateInstance<SyncInboxController>(scope.ServiceProvider);
                var applied = await controller.RetryUnappliedEntriesAsync(stoppingToken);

                if (applied > 0)
                {
                    _logger.LogInformation(
                        "Sync inbox retry: {Applied} previously-stuck entries applied.", applied);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync inbox retry sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("SyncInboxRetryService stopping.");
    }
}
