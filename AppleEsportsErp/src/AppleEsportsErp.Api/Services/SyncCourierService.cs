using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Application.Services;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Api.Services;

public class SyncCourierService : BackgroundService
{
    private readonly ILogger<SyncCourierService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _pollIntervalSeconds = 30;
    private readonly int _maxAttempts = 5;
    private readonly int _batchSize = 100;

    // When each branch's link to Head Office was first seen to be down, so the outage can be
    // reported with a start time rather than just "it failed again". Cleared on the first
    // successful delivery, which is the moment the link came back.
    private readonly Dictionary<Guid, DateTimeOffset> _offlineSince = new();

    public SyncCourierService(
        ILogger<SyncCourierService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _pollIntervalSeconds = configuration.GetValue("Sync:PollIntervalSeconds", 30);
        _maxAttempts = configuration.GetValue("Sync:MaxRetryAttempts", 5);
        _batchSize = configuration.GetValue("Sync:BatchSize", 100);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sync Courier service starting. Poll interval: {IntervalSeconds}s, Max attempts: {MaxAttempts}, Batch size: {BatchSize}",
            _pollIntervalSeconds, _maxAttempts, _batchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in sync courier service");
            }

            await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
        }
    }

    private async Task ProcessOutboxEntriesAsync(CancellationToken cancellationToken)
    {
        using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Fetch unsent entries (max _batchSize at a time)
            var unsentEntries = await context.SyncOutboxEntries
                .Where(e => e.SyncedAt == null && e.AttemptCount < _maxAttempts)
                .OrderBy(e => e.CreatedAt)
                .Take(_batchSize)
                .ToListAsync(cancellationToken);

            if (!unsentEntries.Any())
                return;

            _logger.LogDebug("Processing {Count} unsent sync outbox entries", unsentEntries.Count);

            // Group by branch (send each branch's entries to Head Office separately)
            var groupedByBranch = unsentEntries.GroupBy(e => e.BranchId);

            foreach (var branchGroup in groupedByBranch)
            {
                await SendBranchSyncBatchAsync(context, branchGroup.ToList(), cancellationToken);
            }
        }
    }

    private async Task SendBranchSyncBatchAsync(AppDbContext context, List<SyncOutboxEntry> entries, CancellationToken cancellationToken)
    {
        var branchId = entries.First().BranchId;
        var headOfficeUrl = _configuration["Sync:HeadOfficeUrl"];

        if (string.IsNullOrWhiteSpace(headOfficeUrl))
        {
            _logger.LogWarning("Sync:HeadOfficeUrl not configured, skipping sync for branch {BranchId}", branchId);
            return;
        }

        try
        {
            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                var syncBatch = new
                {
                    branchId,
                    entries = entries.Select(e => new
                    {
                        id = e.Id,
                        aggregateType = e.AggregateType,
                        aggregateId = e.AggregateId,
                        eventType = e.EventType,
                        eventData = JsonSerializer.Deserialize<object>(e.EventData),
                        createdAt = e.CreatedAt
                    }).ToList()
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(syncBatch),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(
                    $"{headOfficeUrl.TrimEnd('/')}/api/sync/receive",
                    content,
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    // Mark all entries as synced
                    foreach (var entry in entries)
                    {
                        entry.SyncedAt = DateTime.UtcNow;
                        entry.AttemptCount++;
                    }

                    // The link is back. Close off the outage before saving, so the record and
                    // the delivery land in the same write.
                    await CloseInternetOutageAsync(context, branchId, cancellationToken);

                    await context.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Successfully synced {Count} entries from branch {BranchId}", entries.Count, branchId);
                }
                else
                {
                    var errorMsg = await response.Content.ReadAsStringAsync(cancellationToken);
                    HandleSyncFailure(context, entries, $"HTTP {response.StatusCode}: {errorMsg}");
                    _logger.LogWarning("Failed to sync {Count} entries from branch {BranchId}: {Status}", entries.Count, branchId, response.StatusCode);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            // Cannot reach Head Office at all — this is the internet being down, as opposed
            // to Head Office answering with an error.
            MarkInternetDown(branchId);
            HandleSyncFailure(context, entries, $"Network error: {ex.Message}");
            _logger.LogWarning(ex, "Network error syncing branch {BranchId}", branchId);
        }
        catch (TaskCanceledException ex)
        {
            MarkInternetDown(branchId);
            HandleSyncFailure(context, entries, $"Request timeout: {ex.Message}");
            _logger.LogWarning(ex, "Timeout syncing branch {BranchId}", branchId);
        }
        catch (Exception ex)
        {
            HandleSyncFailure(context, entries, $"Unexpected error: {ex.Message}");
            _logger.LogError(ex, "Unexpected error syncing branch {BranchId}", branchId);
        }
    }

    /// <summary>
    /// Notes the first moment this branch's link went down. Only the first failure sets the
    /// clock — a link that has been down for an hour should read as one hour-long outage, not
    /// a hundred and twenty separate thirty-second ones.
    /// </summary>
    private void MarkInternetDown(Guid branchId)
    {
        if (!_offlineSince.ContainsKey(branchId))
        {
            _offlineSince[branchId] = DateTimeOffset.UtcNow;
            _logger.LogWarning("Branch {BranchId} has lost its link to Head Office.", branchId);
        }
    }

    /// <summary>
    /// Records a completed internet outage once delivery succeeds again.
    ///
    /// Written only on recovery, because until then we do not know when it ended. Very short
    /// blips are discarded: a single missed poll is normal on any connection, and filling the
    /// owner's report with thirty-second entries would bury the outages that actually mattered.
    ///
    /// Deliberately separate from a power cut. Play was never interrupted here — the branch
    /// ran perfectly on its own and no customer noticed — so nothing is credited back. It is
    /// recorded so the owner knows why a branch's figures arrived late.
    /// </summary>
    private async Task CloseInternetOutageAsync(AppDbContext context, Guid branchId, CancellationToken cancellationToken)
    {
        if (!_offlineSince.TryGetValue(branchId, out var since)) return;
        _offlineSince.Remove(branchId);

        var now = DateTimeOffset.UtcNow;
        var seconds = (int)(now - since).TotalSeconds;

        const int worthReportingSeconds = 120;
        if (seconds < worthReportingSeconds)
        {
            _logger.LogInformation("Branch {BranchId} link restored after {Seconds}s — too brief to report.", branchId, seconds);
            return;
        }

        context.DowntimeEvents.Add(new DowntimeEvent
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Kind = DowntimeKind.InternetOffline,
            StartedAt = since,
            EndedAt = now,
            DurationSeconds = seconds,
            SessionsAffected = 0,   // nobody's game was interrupted
            BusinessDay = IndiaTime.BusinessDayOf(since),
            Notes = "Branch could not reach Head Office. Play continued normally; data was queued and has now been delivered.",
            CreatedAt = now,
        });

        _logger.LogInformation(
            "Branch {BranchId} link restored after {Minutes:N0} minutes offline ({From} to {To} IST) — recorded for the day's report.",
            branchId, seconds / 60.0, IndiaTime.Format(since), IndiaTime.Format(now));
    }

    private void HandleSyncFailure(AppDbContext context, List<SyncOutboxEntry> entries, string errorMsg)
    {
        foreach (var entry in entries)
        {
            entry.AttemptCount++;
            entry.LastError = errorMsg;

            if (entry.AttemptCount >= _maxAttempts)
            {
                _logger.LogError("Sync entry {EntryId} exceeded max attempts ({MaxAttempts}). Last error: {Error}",
                    entry.Id, _maxAttempts, errorMsg);
            }
        }

        context.SaveChanges();
    }
}
