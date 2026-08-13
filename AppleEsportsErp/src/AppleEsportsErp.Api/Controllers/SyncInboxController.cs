using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Application.DTOs.Common;
using AppleEsportsErp.Infrastructure.Data;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;
using AppleEsportsErp.Application.Interfaces;

namespace AppleEsportsErp.Api.Controllers;

/// <summary>
/// Head Office's inbox — receives everything the branches did while they were on their own.
/// </summary>
[ApiController]
[Route("api/sync")]
public class SyncInboxController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly ILogger<SyncInboxController> _logger;

    public SyncInboxController(AppDbContext db, IEmailService email, ILogger<SyncInboxController> logger)
    {
        _db = db;
        _email = email;
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
            //
            // Held is not the same as applied, and treating it as such lost records for good.
            // An entry that was stored but failed to fold in was skipped on every resend, so
            // one failure became permanent — the branch was told "delivered", stopped
            // resending, and the session never appeared at Head Office. Anything not yet
            // applied is retried instead.
            var incomingIds = dto.Entries.Select(e => e.Id).ToList();
            var held = await _db.SyncInboxEntries
                .Where(e => incomingIds.Contains(e.Id))
                .ToListAsync();

            var alreadyApplied = held.Where(e => e.Applied).Select(e => e.Id).ToHashSet();
            var awaitingRetry = held.Where(e => !e.Applied).ToList();

            var stored = new List<SyncInboxEntry>();

            foreach (var entry in dto.Entries)
            {
                if (alreadyApplied.Contains(entry.Id)) continue;
                if (awaitingRetry.Any(h => h.Id == entry.Id)) continue;   // retried below

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

            foreach (var entry in stored.Concat(awaitingRetry))
            {
                try
                {
                    await ApplyToHeadOfficeRecordsAsync(entry);
                    entry.Applied = true;
                    entry.ApplyError = null;

                    // Saved per entry, inside the try. Applying only queues changes — the
                    // insert happens here — so a single save for the whole batch threw
                    // outside this catch, took the entire request down with a 500, and left
                    // the error unrecorded. One bad entry must not cost the good ones.
                    await _db.SaveChangesAsync();
                    appliedCount++;
                }
                catch (Exception ex)
                {
                    // Whatever this entry queued is still tracked and would be retried by the
                    // next save, failing again and taking the rest of the batch with it.
                    DiscardPendingChanges();

                    entry.Applied = false;
                    entry.ApplyError = ex.GetBaseException().Message;

                    _logger.LogError(ex,
                        "Stored but could not apply sync entry {EntryId} ({EventType}) from branch {BranchId}",
                        entry.Id, entry.EventType, dto.BranchId);

                    try { await _db.SaveChangesAsync(); }
                    catch (Exception saveEx)
                    {
                        _logger.LogError(saveEx, "Could not even record why entry {EntryId} failed.", entry.Id);
                    }
                }
            }

            // Retries are counted separately from new arrivals. A batch that is all retries
            // and applies none of them is the signal that something is stuck, and lumping
            // them in with duplicates is what made this invisible in the first place.
            _logger.LogInformation(
                "Sync batch from branch {BranchId}: {Received} received, {Stored} new, {Retried} retried, {Applied} applied, {Duplicate} already applied.",
                dto.BranchId, dto.Entries.Count, stored.Count, awaitingRetry.Count, appliedCount, alreadyApplied.Count);

            return Ok(ApiResponse<object>.Ok(new
            {
                processed = appliedCount,
                stored = stored.Count,
                retried = awaitingRetry.Count,
                duplicates = alreadyApplied.Count,
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
            case "member.created":
                await UpsertMemberAsync(held, root);
                break;

            case "session.started":
                await UpsertSessionStartedAsync(held, root);
                break;

            case "session.stopped":
            case "session.ended":
                await UpsertSessionStoppedAsync(held, root);
                break;

            case "bill.paid":
            case "payment.recorded":
                await RecordPaymentAsync(held, root);
                break;

            case "wallet.topped_up":
            case "member.wallet_toppedup":
                await RecordWalletTopUpAsync(held, root);
                break;

            case "email.send_requested":
                await SendQueuedEmailAsync(held, root);
                break;

            default:
                _logger.LogWarning("No handler for sync event type {EventType}; payload retained.", held.EventType);
                break;
        }
    }

    /// <summary>
    /// Forgets an optional reference Head Office does not have.
    ///
    /// A shift belongs to the branch that opened it and is never sent up — so a session
    /// naming one failed its foreign key, and the whole delivery was lost:
    ///
    ///     insert or update on table "sessions" violates foreign key
    ///     constraint "FK_sessions_shifts_ShiftId"
    ///
    /// Refusing the record over a detail Head Office does not track would be the wrong
    /// trade. Who played, on which PC, for how much, is what matters up here; which of the
    /// operator's shifts it fell in is branch bookkeeping. The session is kept and the
    /// reference dropped, rather than the reverse.
    /// </summary>
    private async Task<Guid?> KnownHereOnly<TEntity>(Guid? id) where TEntity : class
        => id is null || !await _db.Set<TEntity>().AnyAsync(e => EF.Property<Guid>(e, "Id") == id)
            ? null
            : id;

    /// <summary>
    /// Drops everything a failed entry queued, so the next save is not poisoned by it.
    /// Inbox rows are kept — they carry the record of what went wrong.
    /// </summary>
    private void DiscardPendingChanges()
    {
        foreach (var tracked in _db.ChangeTracker.Entries().ToList())
        {
            if (tracked.Entity is SyncInboxEntry) continue;
            if (tracked.State is EntityState.Added or EntityState.Modified)
                tracked.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// A member signed up at a branch, arriving at Head Office.
    ///
    /// Members are the one record a branch legitimately creates that Head Office must also
    /// hold: a person joins at whichever shop they walk into, and their wallet follows them to
    /// the others. Without this the branch's own wallet top-ups arrive naming somebody the
    /// server has never heard of - Rs 1,000 belonging to nobody.
    ///
    /// No balances are set from here. The amounts on this event are what the branch had at the
    /// moment of signing up, which is nothing; the money is a separate story told by wallet
    /// events, and inferring a balance from a creation event is how a stale batch overwrites a
    /// real one.
    /// </summary>
    private async Task UpsertMemberAsync(SyncInboxEntry held, JsonElement root)
    {
        var memberId = held.AggregateId;
        if (await _db.Members.AnyAsync(m => m.Id == memberId))
            return;   // already known: a redelivery, or this box is both branch and Head Office

        var fullName = ReadString(root, "fullName");
        if (string.IsNullOrWhiteSpace(fullName))
            throw new InvalidOperationException($"Member {memberId} arrived with no name.");

        // Not branch-scoped, and correctly so: a member joins at one shop and plays at any of
        // them, which is the whole reason their wallet has to live at Head Office rather than
        // on one till.
        _db.Members.Add(new Member
        {
            Id = memberId,
            FullName = fullName,
            MemberNumber = ReadString(root, "memberNumber") ?? memberId.ToString("N")[..8].ToUpperInvariant(),
            MobileNumber = ReadString(root, "mobileNumber") ?? string.Empty,
            Email = ReadString(root, "email"),
            Username = ReadString(root, "username"),
            Status = MemberStatus.Active,
            CreatedAt = ReadDate(root, "createdAt") ?? held.OccurredAt,
            UpdatedAt = held.ReceivedAt,
        });
    }

    /// <summary>
    /// Head Office's view of what each PC is doing, kept in step with the sessions it is told
    /// about.
    ///
    /// Derived from session events rather than reported separately, deliberately. A PC's state
    /// is set in a dozen places across billing, reservations and maintenance, and instrumenting
    /// every one of them means missing one — and a state that is right most of the time is worse
    /// than one that is plainly derived, because nobody knows which screens to trust.
    ///
    /// Before this, Head Office showed Adajan with three PCs "awaiting billing" whose last
    /// change was in July, against sessions that no longer existed. Four days of fiction on the
    /// screen the owner uses to see the business.
    /// </summary>
    private async Task SetPcStateAsync(Guid? pcId, PcState state, Guid? currentSessionId)
    {
        if (pcId is null) return;

        var pc = await _db.Pcs.FirstOrDefaultAsync(p => p.Id == pcId);
        if (pc is null) return;

        pc.State = state;
        pc.CurrentSessionId = currentSessionId;
        pc.LastActiveAt = DateTimeOffset.UtcNow;
        pc.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Sends an email a branch could not send itself.
    ///
    /// Only Head Office holds the mail credentials, so a branch queues instead of sending — the
    /// welcome message, the password reset link, the low stock warning. That design was right
    /// and the last step was missing: these arrived here and fell through to "no handler",
    /// which logged a warning and marked the entry applied. Every email any branch has ever
    /// queued was accepted and thrown away, and the branch was told it had been delivered.
    ///
    /// Found because a member signed up at a branch and no reset link ever came.
    ///
    /// A failure to send throws, which leaves the entry unapplied and retried. Mail that
    /// bounces is a different problem from mail never attempted, and only the second is worth
    /// retrying.
    /// </summary>
    private async Task SendQueuedEmailAsync(SyncInboxEntry held, JsonElement root)
    {
        var to = ReadString(root, "to");
        var subject = ReadString(root, "subject");
        var body = ReadString(root, "body");

        if (string.IsNullOrWhiteSpace(to))
        {
            // Nothing to retry towards. Swallowed rather than thrown so it does not sit in the
            // queue for ever being reattempted at nobody.
            _logger.LogWarning("A branch queued an email with no recipient. Discarded.");
            return;
        }

        await _email.SendEmailAsync(to, subject ?? "Apple Esports", body ?? string.Empty);

        _logger.LogInformation(
            "Sent an email queued by branch {BranchId} to {Recipient}.", held.BranchId, to);
    }

    /// <summary>
    /// Money a branch actually took, appearing in Head Office's own figures.
    ///
    /// The previous version deliberately refused to do this, and its reasoning was sound as far
    /// as it went: a bill is a tree of line items and discounts, and rebuilding one from a flat
    /// event produces something that looks like a bill and does not reconcile. But refusing
    /// meant the entry was marked applied while nothing was written, so a branch's takings never
    /// reached the End of Day screen and nothing anywhere said so. Head Office showed a shop
    /// that traded all evening as having taken nothing.
    ///
    /// The distinction that resolves it: a PAYMENT is flat. Cash, online, wallet, when, which
    /// branch - facts, not a tree. That is also exactly what the End of Day screen reads. So the
    /// payment is recorded in full, and the bill it belongs to is written as a header carrying
    /// the branch's own totals, with no invented line items. The items stay at the branch, which
    /// is where they were rung up and where they can be looked at.
    ///
    /// The branch's own identifiers are used throughout, so the same bill is one bill in both
    /// places and a redelivery cannot double the takings.
    /// </summary>
    private async Task RecordPaymentAsync(SyncInboxEntry held, JsonElement root)
    {
        var billId = ReadGuid(root, "billId") ?? held.AggregateId;

        if (await _db.Payments.AnyAsync(p => p.BillId == billId))
            return;   // already counted

        var operatorId = ReadGuid(root, "operatorId");
        if (operatorId is null || !await _db.Operators.AnyAsync(o => o.Id == operatorId))
            throw new InvalidOperationException(
                $"Head Office has no operator {operatorId}, so this payment cannot be attributed.");

        var gaming = ReadDecimal(root, "gamingAmount") ?? 0m;
        var food = ReadDecimal(root, "foodAmount") ?? 0m;
        var discount = ReadDecimal(root, "discountAmount") ?? 0m;
        var billTotal = ReadDecimal(root, "billTotal") ?? (gaming + food - discount);
        var totalPaid = ReadDecimal(root, "totalPaid") ?? billTotal;
        var cash = ReadDecimal(root, "cashAmount") ?? 0m;
        var paidAt = ReadDate(root, "paidAt") ?? held.OccurredAt;

        Enum.TryParse<PaymentType>(ReadString(root, "paymentType"), ignoreCase: true, out var method);

        // Anything not taken in cash is whatever the branch settled it with. Split out this way
        // rather than guessed per method, because the drawer only ever cares about the cash.
        var nonCash = Math.Max(0m, totalPaid - cash);

        if (!await _db.Bills.AnyAsync(b => b.Id == billId))
        {
            _db.Bills.Add(new Bill
            {
                Id = billId,
                BillNumber = ReadString(root, "billNumber") ?? billId.ToString("N")[..8].ToUpperInvariant(),
                BranchId = held.BranchId,
                OperatorId = operatorId.Value,
                SessionId = await KnownHereOnly<Session>(ReadGuid(root, "sessionId")),
                ShiftId = await KnownHereOnly<Shift>(ReadGuid(root, "shiftId")),
                GamingAmount = gaming,
                FoodAmount = food,
                Subtotal = gaming + food,
                DiscountAmount = discount,
                TotalAmount = billTotal,
                PaymentType = method,
                Status = BillStatus.Completed,
                CreatedAt = paidAt,
                CompletedAt = paidAt,
            });
        }

        _db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            BillId = billId,
            BranchId = held.BranchId,
            OperatorId = operatorId.Value,
            PaymentType = method,
            TotalAmount = totalPaid,
            CashAmount = cash,
            OnlineAmount = method == PaymentType.Wallet ? 0m : nonCash,
            WalletAmount = method == PaymentType.Wallet ? nonCash : 0m,
            CashReceived = cash,
            ChangeReturned = 0m,
            ActualCashCollected = cash,
            GamingPortion = gaming,
            FoodPortion = food,
            Status = "completed",
            CreatedAt = paidAt,
        });
    }

    /// <summary>
    /// A wallet top-up taken at a branch, recorded at Head Office.
    ///
    /// The earlier refusal was right about the danger and wrong about the remedy. Inferring a
    /// balance from a single event IS how out-of-order delivery corrupts real money - but
    /// dropping the event entirely means Rs 1,000 taken across the counter exists nowhere up
    /// here at all, which is worse.
    ///
    /// So the transaction is recorded, because it is a fact that happened at a known time for a
    /// known amount. The member's stored balance is left alone: the branch took the money and
    /// the branch owns that figure until there is a deliberate reconciliation. Head Office can
    /// see and report every top-up without pretending to be the authority on the balance.
    /// </summary>
    private async Task RecordWalletTopUpAsync(SyncInboxEntry held, JsonElement root)
    {
        // The branch's own transaction id, so a redelivery is the same row rather than a second
        // one. Without it the same Rs 1,000 could be counted twice in a monthly total.
        var txId = ReadGuid(root, "walletTransactionId") ?? held.AggregateId;
        if (await _db.WalletTransactions.AnyAsync(w => w.Id == txId))
            return;

        var memberId = ReadGuid(root, "memberId") ?? held.AggregateId;
        if (!await _db.Members.AnyAsync(m => m.Id == memberId))
            throw new InvalidOperationException(
                $"Head Office has no member {memberId}. The member.created event should arrive first.");

        var cash = ReadDecimal(root, "cashAmount") ?? 0m;
        var bonus = ReadDecimal(root, "bonusAmount") ?? 0m;
        var credited = ReadDecimal(root, "totalCredit") ?? (cash + bonus);
        var balanceAfter = ReadDecimal(root, "gamingBalanceAfter") ?? 0m;

        _db.WalletTransactions.Add(new WalletTransaction
        {
            Id = txId,
            MemberId = memberId,
            BranchId = held.BranchId,
            OperatorId = await KnownHereOnly<Operator>(ReadGuid(root, "operatorId")),
            ShiftId = await KnownHereOnly<Shift>(ReadGuid(root, "shiftId")),
            Action = WalletAction.Recharge,
            TargetWallet = WalletType.Gaming,
            Amount = credited,
            BalanceBefore = balanceAfter - credited,
            BalanceAfter = balanceAfter,
            PaymentType = ReadString(root, "paymentType"),
            CashAmount = cash,
            BonusAmount = bonus,
            CreatedAt = ReadDate(root, "occurredAt") ?? held.OccurredAt,
        });
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

        var startTime = ReadDate(root, "startTime") ?? held.OccurredAt;
        var plannedMin = ReadInt(root, "plannedDurationMin");

        _db.Sessions.Add(new Session
        {
            Id = sessionId,
            BranchId = held.BranchId,
            PcId = pcId.Value,
            OperatorId = operatorId.Value,
            ShiftId = await KnownHereOnly<Shift>(ReadGuid(root, "shiftId")),
            MemberId = await KnownHereOnly<Member>(ReadGuid(root, "memberId")),
            CustomerName = ReadString(root, "customerName"),
            StartTime = startTime,
            PlannedDurationMin = plannedMin,

            // When the session was sold with a length, work out when it is due to end.
            //
            // This is why Head Office showed an hour's play as unlimited. The duration arrived
            // and was stored faithfully, but EndTime was left null - and the PC grid decides
            // pay-as-you-go purely by "has no end time". So every synced session, however it
            // was sold, drew the infinity symbol and no countdown. The branch's own screen was
            // right the whole time, which is what made the two impossible to reconcile.
            //
            // Computed rather than sent, because the branch already sends both halves and a
            // number derived twice from the same two values cannot disagree with itself.
            // Genuine pay-as-you-go has no planned duration and correctly stays null here.
            EndTime = plannedMin is > 0 ? startTime.AddMinutes(plannedMin.Value) : null,

            GamingType = ReadString(root, "gamingType") ?? "standard",
            TotalAmount = ReadDecimal(root, "expectedAmount") ?? 0m,
            GamingAmount = ReadDecimal(root, "expectedAmount") ?? 0m,
            State = SessionState.Active,
            CreatedAt = held.OccurredAt,
            UpdatedAt = held.ReceivedAt,
        });

        await SetPcStateAsync(pcId, PcState.Active, sessionId);
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

        // The PC is free again as far as Head Office is concerned. Whether the branch shows it
        // as awaiting billing is the branch's own business - that state exists to tell the
        // operator standing there to collect money, and there is nobody standing at Head Office.
        await SetPcStateAsync(session.PcId, PcState.Idle, currentSessionId: null);
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
