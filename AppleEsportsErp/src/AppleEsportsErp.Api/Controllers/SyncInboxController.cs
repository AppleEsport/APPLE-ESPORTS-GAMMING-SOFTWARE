using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Head Office's inbox — receives everything the branches did while they were on their own.
/// </summary>
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

    /// <summary>
    /// Receives a batch of events from a branch.
    ///
    /// Every event is committed to the inbox verbatim <b>before</b> any attempt is made to
    /// interpret it. That ordering is the whole point: a branch marks its entries delivered
    /// once we answer 200, so acknowledging data we did not actually keep destroys it for
    /// good. The previous implementation did exactly that — every handler was either a no-op
    /// or looked up a record Head Office had never been sent, so batches were acknowledged
    /// and silently discarded.
    /// </summary>
    [HttpPost("receive")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveSyncBatch([FromBody] ReceiveSyncBatchDto dto)
    {
        if (dto?.Entries == null || dto.Entries.Count == 0)
        {
            _logger.LogWarning("Received empty sync batch from branch {BranchId}", dto?.BranchId);
            return Ok(ApiResponse<object>.Ok(new { processed = 0, total = 0, branchId = dto?.BranchId }));
        }

        try
        {
            var receivedAt = DateTimeOffset.UtcNow;

            // Redelivery is normal: a branch that never saw our response will send again.
            // Skip what we already hold rather than double-counting it.
            var incomingIds = dto.Entries.Select(e => e.Id).ToList();
            var alreadyHeld = (await _db.SyncInboxEntries
                .Where(e => incomingIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync()).ToHashSet();

            var stored = new List<SyncInboxEntry>();

            foreach (var entry in dto.Entries)
            {
                if (alreadyHeld.Contains(entry.Id)) continue;

                stored.Add(new SyncInboxEntry
                {
                    Id = entry.Id,
                    BranchId = dto.BranchId,
                    AggregateType = entry.AggregateType ?? "Unknown",
                    AggregateId = entry.AggregateId,
                    EventType = entry.EventType ?? "unknown",
                    EventData = JsonSerializer.Serialize(entry.EventData),
                    // Normalised to UTC on the way in. Postgres "timestamp with time zone"
                    // accepts nothing else, and a branch in Surat legitimately sends +05:30 —
                    // left alone it takes down the whole batch with an Npgsql ArgumentException.
                    // This is the boundary where untrusted timestamps arrive, so it is the
                    // right place to fix them rather than trusting every branch to send UTC.
                    OccurredAt = entry.CreatedAt.ToUniversalTime(),
                    ReceivedAt = receivedAt,
                    Applied = false,
                });
            }

            if (stored.Count > 0)
            {
                _db.SyncInboxEntries.AddRange(stored);
                // Durable first. Nothing below may cost us what we have just accepted.
                await _db.SaveChangesAsync();
            }

            var appliedCount = 0;

            foreach (var held in stored)
            {
                try
                {
                    await ApplyToHeadOfficeRecordsAsync(held);
                    held.Applied = true;
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    // The payload is already safe. Record why it could not be folded in and
                    // carry on — it can be replayed without the branch resending anything.
                    held.ApplyError = ex.Message;
                    _logger.LogError(ex,
                        "Stored but could not apply sync entry {EntryId} ({EventType}) from branch {BranchId}",
                        held.Id, held.EventType, dto.BranchId);
                }
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Sync batch from branch {BranchId}: {Received} received, {Stored} new, {Applied} applied, {Duplicate} already held.",
                dto.BranchId, dto.Entries.Count, stored.Count, appliedCount, alreadyHeld.Count);

            return Ok(ApiResponse<object>.Ok(new
            {
                processed = appliedCount,
                stored = stored.Count,
                duplicates = alreadyHeld.Count,
                total = dto.Entries.Count,
                branchId = dto.BranchId
            }));
        }
        catch (Exception ex)
        {
            // Fail loudly. A 500 makes the branch keep the batch and retry, which is the safe
            // outcome — a repeated delivery is recoverable, a lost one is not.
            _logger.LogError(ex, "Critical error in sync inbox receiver for branch {BranchId}", dto.BranchId);
            return StatusCode(500, ApiResponse<object>.Fail("Sync processing failed", "SYNC_ERROR"));
        }
    }

    /// <summary>
    /// Folds a stored event into Head Office's own records so it appears on the dashboard.
    ///
    /// Throwing here is safe and expected: the payload is already committed, so a failure
    /// marks that one entry unapplied rather than losing it or rejecting the batch. The usual
    /// cause is a branch reporting against a PC or operator Head Office has no row for, which
    /// resolves once the branch is provisioned from Head Office and both share identifiers.
    /// </summary>
    private async Task ApplyToHeadOfficeRecordsAsync(SyncInboxEntry held)
    {
        using var payload = JsonDocument.Parse(held.EventData);
        var root = payload.RootElement;

        switch (held.EventType.ToLowerInvariant())
        {
            case "session.started":
                await UpsertSessionStartedAsync(held, root);
                break;

            case "session.stopped":
            case "session.ended":
                await UpsertSessionStoppedAsync(held, root);
                break;

            case "bill.paid":
            case "payment.recorded":
                // Deliberately not reconstructed into the bills table. A bill is a tree of
                // line items, discounts and payments; rebuilding it from a single flat event
                // would produce something that looks like a bill but does not reconcile.
                // Held intact for reporting and for a purpose-built importer later.
                _logger.LogInformation(
                    "Bill event {EventType} from branch {BranchId} stored for reporting (bill {BillId}).",
                    held.EventType, held.BranchId, held.AggregateId);
                break;

            case "wallet.topped_up":
            case "member.wallet_toppedup":
                // Same reasoning, higher stakes: Head Office must never infer a wallet balance
                // from one event, because an out-of-order batch would corrupt real money.
                _logger.LogInformation(
                    "Wallet event from branch {BranchId} stored for reporting (member {MemberId}).",
                    held.BranchId, held.AggregateId);
                break;

            default:
                _logger.LogWarning("No handler for sync event type {EventType}; payload retained.", held.EventType);
                break;
        }
    }

    private async Task UpsertSessionStartedAsync(SyncInboxEntry held, JsonElement root)
    {
        var sessionId = held.AggregateId;
        if (await _db.Sessions.AnyAsync(s => s.Id == sessionId))
            return;   // already known: a redelivery, or this box is both branch and Head Office

        var pcId = ReadGuid(root, "pcId");
        var operatorId = ReadGuid(root, "operatorId");

        // Head Office can only hold a session pointing at a PC and operator it knows about.
        // Refusing loudly is right — a silently orphaned session would quietly skew reports.
        if (pcId is null || !await _db.Pcs.AnyAsync(p => p.Id == pcId))
            throw new InvalidOperationException(
                $"Head Office has no PC {pcId} for branch {held.BranchId}. " +
                "Provision the branch from Head Office so both sides share identifiers.");

        if (operatorId is null || !await _db.Operators.AnyAsync(o => o.Id == operatorId))
            throw new InvalidOperationException($"Head Office has no operator {operatorId}.");

        _db.Sessions.Add(new Session
        {
            Id = sessionId,
            BranchId = held.BranchId,
            PcId = pcId.Value,
            OperatorId = operatorId.Value,
            ShiftId = ReadGuid(root, "shiftId"),
            MemberId = ReadGuid(root, "memberId"),
            CustomerName = ReadString(root, "customerName"),
            StartTime = ReadDate(root, "startTime") ?? held.OccurredAt,
            PlannedDurationMin = ReadInt(root, "plannedDurationMin"),
            GamingType = ReadString(root, "gamingType") ?? "standard",
            TotalAmount = ReadDecimal(root, "expectedAmount") ?? 0m,
            GamingAmount = ReadDecimal(root, "expectedAmount") ?? 0m,
            State = SessionState.Active,
            CreatedAt = held.OccurredAt,
            UpdatedAt = held.ReceivedAt,
        });
    }

    private async Task UpsertSessionStoppedAsync(SyncInboxEntry held, JsonElement root)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == held.AggregateId);

        if (session is null)
        {
            // A stop can arrive without its start if the branch was offline when the session
            // began and the backlog was split across batches. The stop event carries enough
            // to rebuild the whole thing.
            await UpsertSessionStartedAsync(held, root);
            await _db.SaveChangesAsync();
            session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == held.AggregateId);
            if (session is null) return;
        }

        session.State = SessionState.Completed;
        session.EndTime = ReadDate(root, "endTime");
        session.ActualDurationMin = ReadInt(root, "billedMinutes");
        session.PausedSeconds = ReadInt(root, "pausedSeconds") ?? 0;
        session.GamingAmount = ReadDecimal(root, "gamingAmount") ?? session.GamingAmount;
        session.FoodAmount = ReadDecimal(root, "foodAmount") ?? session.FoodAmount;
        session.TotalAmount = ReadDecimal(root, "totalAmount") ?? session.TotalAmount;
        session.UpdatedAt = held.ReceivedAt;
    }

    // ── Payload readers ──
    // Tolerant on purpose: a branch running a slightly older build must not render a whole
    // batch unprocessable, so a missing or malformed field simply reads as null.

    private static Guid? ReadGuid(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && Guid.TryParse(v.GetString(), out var g) ? g : null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetInt32(out var i) ? i : null;

    private static decimal? ReadDecimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
        && v.TryGetDecimal(out var d) ? d : null;

    // Always returns UTC. Branch payloads carry local offsets (+05:30), and every one of
    // these values ends up in a "timestamp with time zone" column that accepts only UTC.
    private static DateTimeOffset? ReadDate(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(v.GetString(), out var t) ? t.ToUniversalTime() : null;
}

public class ReceiveSyncBatchDto
{
    public Guid BranchId { get; set; }
    public List<SyncEntryDto> Entries { get; set; } = new();
}

public class SyncEntryDto
{
    public Guid Id { get; set; }
    public string? AggregateType { get; set; }
    public Guid AggregateId { get; set; }
    public string? EventType { get; set; }
    public object? EventData { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
