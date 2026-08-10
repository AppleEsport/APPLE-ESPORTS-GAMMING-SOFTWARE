using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Api.Services;

public class SyncCourierService : BackgroundService
{
    private readonly ILogger<SyncCourierService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _pollIntervalSeconds = 30;
    private readonly int _maxAttempts = 5;
    private readonly int _batchSize = 100;

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
            HandleSyncFailure(context, entries, $"Network error: {ex.Message}");
            _logger.LogWarning(ex, "Network error syncing branch {BranchId}", branchId);
        }
        catch (TaskCanceledException ex)
        {
            HandleSyncFailure(context, entries, $"Request timeout: {ex.Message}");
            _logger.LogWarning(ex, "Timeout syncing branch {BranchId}", branchId);
        }
        catch (Exception ex)
        {
            HandleSyncFailure(context, entries, $"Unexpected error: {ex.Message}");
            _logger.LogError(ex, "Unexpected error syncing branch {BranchId}", branchId);
        }
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
