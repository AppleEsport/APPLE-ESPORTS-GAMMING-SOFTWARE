using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Infrastructure.Data;

/// <summary>
/// Records what a branch did, automatically, as it is being saved.
///
/// Every sync bug in this system has had the same shape. Somebody added a feature, wired an
/// outbox event into the one service that happened to change that record, and missed another
/// place that changed the same record. Sessions were remembered. Bills were remembered. PC
/// state was not, so Head Office showed early August for four days. Operator status was not,
/// so a branch trading all evening showed its staff as logged out. Wallet spending was not,
/// so Head Office's balances could only ever climb.
///
/// Shifts, cash registers and customer credits are the same story again, and worse: they are
/// written from at least ten different places between AuthService, CashDeskService,
/// CashRegisterService, BillingService, SessionService, CreditService and ShiftTakeoverService.
/// Hand-wiring ten call sites means missing one, and a missed one is silent - the figures at
/// Head Office are simply wrong, with nothing anywhere saying so. That is exactly the
/// "EOD and Credits do not match" report this exists to answer.
///
/// So this is not wired per service. It watches the change tracker, and anything on the list
/// below is recorded no matter which code path wrote it - including code written next year by
/// somebody who has never heard of sync. Missing a record now requires deleting a line from
/// this file rather than forgetting to add one somewhere else.
///
/// It runs inside the same SaveChanges as the data, so the record and the fact that it
/// happened commit together or not at all. A branch cannot end up having taken money it has
/// no intention of ever reporting.
/// </summary>
public static class SyncCapture
{
    /// <summary>
    /// False at Head Office, which has nowhere to report to and would otherwise queue an
    /// outbox entry for every row it ever wrote - including the rows it just received from a
    /// branch, which would then be sent back to itself for ever.
    ///
    /// Set once at startup from configuration. A deployment does not change role while it is
    /// running, so this is genuinely constant for the life of the process.
    /// </summary>
    public static bool IsEnabled { get; set; }

    /// <summary>
    /// What a branch must tell Head Office about, and the event name each one travels under.
    ///
    /// Deliberately a short, explicit list rather than "everything". Most tables here are
    /// either local bookkeeping (audit logs, sync plumbing), already covered by their own
    /// richer events (sessions, bills, payments carry more than a row snapshot), or state
    /// rather than history and therefore the heartbeat's job (PCs, operators).
    ///
    /// These four are the ones End of Day is computed from, and the reason its figures could
    /// never match: Head Office simply did not have them.
    /// </summary>
    private static readonly Dictionary<Type, string> Watched = new()
    {
        [typeof(Shift)] = "shift",
        [typeof(CashRegister)] = "cash_register",
        [typeof(CashTransaction)] = "cash_transaction",
        [typeof(CustomerCredit)] = "customer_credit",

        // Bills, because a bill only reached Head Office when somebody paid it.
        //
        // The bill.paid event creates the bill as a side effect of recording a payment, which
        // covers every bill that was settled and none of the ones that were not. But a customer
        // credit is raised precisely when a bill is NOT paid - the customer left owing money -
        // and it holds a required reference to that bill. So the one case that creates a credit
        // is the same case that guarantees its bill never arrives, and the credit could never
        // be filed: "insert or update on CustomerCredits violates foreign key
        // FK_CustomerCredits_bills_BillId", found by testing this before shipping it.
        //
        // Unpaid money is the half of the ledger an owner most needs to see, and it was the
        // half that structurally could not arrive.
        [typeof(Bill)] = "bill",
    };

    /// <summary>
    /// Turns every watched insert or update in this save into outbox entries.
    ///
    /// Called before SaveChanges completes, so the entries join the same transaction. Returns
    /// them rather than adding them directly so the caller controls when they enter the
    /// change tracker - adding to a collection being enumerated is how this would otherwise
    /// throw halfway through somebody's shift close.
    /// </summary>
    public static List<SyncOutboxEntry> Collect(ChangeTracker tracker)
    {
        var entries = new List<SyncOutboxEntry>();
        if (!IsEnabled) return entries;

        foreach (var entity in tracker.Entries().ToList())
        {
            if (entity.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (!Watched.TryGetValue(entity.Entity.GetType(), out var aggregate)) continue;

            var id = ReadGuid(entity, "Id");
            var branchId = ReadGuid(entity, "BranchId");

            // Without both, Head Office has nothing to file this against. Skipped rather than
            // thrown: a missing id is a bug worth finding, but not one worth refusing to close
            // somebody's till over.
            if (id is null || branchId is null) continue;

            entries.Add(new SyncOutboxEntry
            {
                Id = Guid.NewGuid(),
                BranchId = branchId.Value,
                AggregateType = aggregate,
                AggregateId = id.Value,

                // "changed" rather than created/updated. Head Office applies these as an
                // upsert keyed on the row's own id, so it does not matter which it was - and
                // a branch that was offline may deliver an update before Head Office has ever
                // seen the insert.
                EventType = $"{aggregate}.changed",
                EventData = Snapshot(entity),
                CreatedAt = DateTime.UtcNow,
            });
        }

        return entries;
    }

    /// <summary>
    /// The whole row as JSON, every scalar column of it.
    ///
    /// Whole rather than just what changed, because Head Office may never have seen this row
    /// before - a branch that spent the evening offline delivers its shift close without the
    /// shift open having arrived first. A complete snapshot applies correctly either way,
    /// where a delta would need a history Head Office does not have.
    ///
    /// Navigation properties are skipped, so a cash register does not drag its branch, its
    /// operator and their entire shift along with it.
    /// </summary>
    private static string Snapshot(EntityEntry entity)
    {
        var values = new Dictionary<string, object?>();

        foreach (var property in entity.Properties)
        {
            // xmin and friends. Several of these tables use PostgreSQL's own row version as a
            // concurrency token, which is a system column belonging to whichever database the
            // row happens to live in - it means nothing at the other end and cannot be written
            // there. Sending it would only invite the receiver to try.
            if (property.Metadata.IsConcurrencyToken) continue;

            // Only genuinely computed columns are skipped - ones the database recalculates on
            // every write, which the sender has no authority over.
            //
            // Emphatically NOT "anything with a default". OpeningBalance, TotalCashSales and
            // ExpectedDrawerCash are all declared HasDefaultValue(0m), which marks them
            // ValueGenerated.OnAdd - and an earlier version of this skipped them on that basis,
            // so a till holding Rs 3,450 was faithfully reported as holding nothing. A default
            // says what to use when no value is given; it never means the value cannot be sent.
            if (property.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAddOrUpdate)
                continue;

            var value = property.CurrentValue;

            values[property.Metadata.Name] = value switch
            {
                null => null,
                DateTimeOffset d => d.ToUniversalTime(),   // Npgsql refuses a non-UTC offset
                DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                Enum e => e.ToString(),
                _ => value,
            };
        }

        return JsonSerializer.Serialize(values);
    }

    private static Guid? ReadGuid(EntityEntry entity, string propertyName)
    {
        var property = entity.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
        return property?.CurrentValue is Guid g && g != Guid.Empty ? g : null;
    }
}
