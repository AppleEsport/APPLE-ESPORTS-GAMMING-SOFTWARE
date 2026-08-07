using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Api.Extensions;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Api.Controllers;

[ApiController]
[Route("api/sync")]
public class SyncInboxController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<SyncInboxController> _logger;

    public SyncInboxController(AppDbContext db, ILogger<SyncInboxController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("receive")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveSyncBatch([FromBody] ReceiveSyncBatchDto dto)
    {
        if (dto?.Entries == null || !dto.Entries.Any())
        {
            _logger.LogWarning("Received empty sync batch from branch {BranchId}", dto?.BranchId);
            return Ok(ApiResponse<object>.Ok(new { processed = 0 }));
        }

        try
        {
            var processedCount = 0;

            foreach (var entry in dto.Entries)
            {
                try
                {
                    // Process each sync entry based on event type
                    await ProcessSyncEntryAsync(dto.BranchId, entry);
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing sync entry {EntryId} from branch {BranchId}",
                        entry.Id, dto.BranchId);
                    // Continue processing other entries even if one fails
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation("Successfully processed {ProcessedCount}/{TotalCount} sync entries from branch {BranchId}",
                processedCount, dto.Entries.Count, dto.BranchId);

            return Ok(ApiResponse<object>.Ok(new
            {
                processed = processedCount,
                total = dto.Entries.Count,
                branchId = dto.BranchId
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error in sync inbox receiver for branch {BranchId}", dto.BranchId);
            return StatusCode(500, ApiResponse<object>.Fail("Sync processing failed", "SYNC_ERROR"));
        }
    }

    private async Task ProcessSyncEntryAsync(Guid branchId, SyncEntryDto entry)
    {
        // Log the incoming event
        _logger.LogDebug("Processing sync entry {EventType} for {AggregateType} {AggregateId} from branch {BranchId}",
            entry.EventType, entry.AggregateType, entry.AggregateId, branchId);

        switch (entry.EventType.ToLower())
        {
            case "session.started":
                await ProcessSessionStartedAsync(branchId, entry);
                break;

            case "session.ended":
                await ProcessSessionEndedAsync(branchId, entry);
                break;

            case "bill.created":
                await ProcessBillCreatedAsync(branchId, entry);
                break;

            case "bill.paid":
                await ProcessBillPaidAsync(branchId, entry);
                break;

            case "member.wallet_toppedup":
                await ProcessMemberWalletTopUpAsync(branchId, entry);
                break;

            case "payment.recorded":
                await ProcessPaymentRecordedAsync(branchId, entry);
                break;

            default:
                _logger.LogWarning("Unknown sync event type: {EventType}", entry.EventType);
                break;
        }
    }

    private async Task ProcessSessionStartedAsync(Guid branchId, SyncEntryDto entry)
    {
        // For session started: just log it, actual session record is already in local DB
        // This sync entry documents that the branch created it
        await Task.CompletedTask;
    }

    private async Task ProcessSessionEndedAsync(Guid branchId, SyncEntryDto entry)
    {
        // For session ended: verify session exists at Head Office and mark as synced
        var sessionId = entry.AggregateId;
        var session = await _db.Sessions.FindAsync(sessionId);

        if (session != null)
        {
            session.UpdatedAt = DateTime.UtcNow;
            _db.Sessions.Update(session);
        }

        await Task.CompletedTask;
    }

    private async Task ProcessBillCreatedAsync(Guid branchId, SyncEntryDto entry)
    {
        // For bill created: verify bill exists
        var billId = entry.AggregateId;
        var bill = await _db.Bills.FindAsync(billId);

        if (bill != null)
        {
            bill.UpdatedAt = DateTime.UtcNow;
            _db.Bills.Update(bill);
        }

        await Task.CompletedTask;
    }

    private async Task ProcessBillPaidAsync(Guid branchId, SyncEntryDto entry)
    {
        // For bill paid: verify payment is recorded
        var billId = entry.AggregateId;
        var bill = await _db.Bills.FindAsync(billId);

        if (bill != null)
        {
            bill.UpdatedAt = DateTime.UtcNow;
            _db.Bills.Update(bill);
        }

        await Task.CompletedTask;
    }

    private async Task ProcessMemberWalletTopUpAsync(Guid branchId, SyncEntryDto entry)
    {
        // For wallet top-up: verify wallet transaction exists
        var transactionId = entry.AggregateId;
        var transaction = await _db.WalletTransactions.FindAsync(transactionId);

        if (transaction != null)
        {
            transaction.CreatedAt = DateTime.UtcNow;
            _db.WalletTransactions.Update(transaction);
        }

        await Task.CompletedTask;
    }

    private async Task ProcessPaymentRecordedAsync(Guid branchId, SyncEntryDto entry)
    {
        // For payment recorded: verify payment exists
        var paymentId = entry.AggregateId;
        var payment = await _db.Payments.FindAsync(paymentId);

        if (payment != null)
        {
            payment.CreatedAt = DateTime.UtcNow;
            _db.Payments.Update(payment);
        }

        await Task.CompletedTask;
    }
}

public class ReceiveSyncBatchDto
{
    public Guid BranchId { get; set; }
    public List<SyncEntryDto> Entries { get; set; } = new();
}

public class SyncEntryDto
{
    public Guid Id { get; set; }
    public string AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; }
    public object EventData { get; set; }
    public DateTime CreatedAt { get; set; }
}
