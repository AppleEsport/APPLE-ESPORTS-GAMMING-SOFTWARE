using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Api.Services;

/// <summary>
/// Closes a trading day that is over when nobody closed it, and sends its report.
///
/// The end-of-day report used to hang entirely on an operator remembering to tick "last shift of
/// the day" before they went home. Forgetting cost the whole day's report and left the register
/// open past 06:00 permanently — the thirty stale registers cleared off the live system all came
/// from exactly that. A tired operator at 3am should not be able to cost the owner a day's report.
///
/// The tick still works and still closes the day early. This is the floor underneath it.
/// </summary>
public class TradingDayCloserService : BackgroundService
{
    /// <summary>
    /// Quarter-hourly. The work is a single indexed query that finds nothing almost every time,
    /// and a day whose report is late by up to fifteen minutes is no worse off — whereas checking
    /// only once, at 06:00 exactly, would skip the day entirely if the server happened to be
    /// restarting at that moment.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private readonly IServiceProvider _services;
    private readonly ILogger<TradingDayCloserService> _logger;

    public TradingDayCloserService(IServiceProvider services, ILogger<TradingDayCloserService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradingDayCloserService is starting.");

        // A short wait before the first pass. On startup the rest of the application is still
        // coming up, and a day left open overnight has already waited hours - another minute
        // costs nothing next to running this against a half-ready system.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();

                var closed = await auth.CloseFinishedTradingDaysAsync(stoppingToken);

                if (closed > 0)
                    _logger.LogWarning("Closed {Count} trading day(s) that nobody had closed.", closed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Swallowed so the loop survives. A failure here must not take the background
                // worker down permanently and quietly stop closing days from then on.
                _logger.LogError(ex, "Failed while closing finished trading days.");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("TradingDayCloserService is stopping.");
    }
}
