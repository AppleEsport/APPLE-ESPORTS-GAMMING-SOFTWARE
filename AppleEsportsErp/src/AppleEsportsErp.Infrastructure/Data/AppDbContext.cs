using Microsoft.EntityFrameworkCore;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Infrastructure.Data;

/// <summary>
/// Gaming Café ERP — EF Core DbContext
/// Maps all 23 tables from schema.sql with full relational integrity.
/// SOP §23: Database Architecture
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── 23 DbSets matching schema.sql ──
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<PricingProfile> PricingProfiles => Set<PricingProfile>();
    public DbSet<Pc> Pcs => Set<Pc>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionActivity> SessionActivities => Set<SessionActivity>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<BillItem> BillItems => Set<BillItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CustomerCredit> CustomerCredits => Set<CustomerCredit>();
    public DbSet<CashRegister> CashRegisters => Set<CashRegister>();
    public DbSet<CashTransaction> CashTransactions => Set<CashTransaction>();
    public DbSet<DenominationCount> DenominationCounts => Set<DenominationCount>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<BranchHeartbeat> BranchHeartbeats => Set<BranchHeartbeat>();
    public DbSet<BranchCommand> BranchCommands => Set<BranchCommand>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryLog> InventoryLogs => Set<InventoryLog>();
    public DbSet<FoodOrder> FoodOrders => Set<FoodOrder>();
    public DbSet<FoodOrderItem> FoodOrderItems => Set<FoodOrderItem>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<LoyaltyPoint> LoyaltyPoints => Set<LoyaltyPoint>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<EodSnapshot> EodSnapshots => Set<EodSnapshot>();

    // Decentralized LAN Offline Architecture sync tables
    public DbSet<OfflineSyncSession> OfflineSyncSessions => Set<OfflineSyncSession>();
    public DbSet<OfflineSyncBilling> OfflineSyncBillings => Set<OfflineSyncBilling>();

    // HR Module
    public DbSet<Employee> Employees => Set<Employee>();

    // Maintenance Logs
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();

    // Version tracking & updates
    public DbSet<VersionInfo> VersionInfos => Set<VersionInfo>();
    public DbSet<BranchVersionStatus> BranchVersionStatuses => Set<BranchVersionStatus>();

    // Sync engine — outbox is what a branch owes Head Office, inbox is what Head Office
    // has been told by its branches.
    public DbSet<SyncOutboxEntry> SyncOutboxEntries => Set<SyncOutboxEntry>();
    public DbSet<SyncInboxEntry> SyncInboxEntries => Set<SyncInboxEntry>();

    // Power cuts and lost connections, for the EOD and printed reports
    public DbSet<DowntimeEvent> DowntimeEvents => Set<DowntimeEvent>();

    /// <summary>
    /// Records anything a branch did that Head Office needs, in the same breath as doing it.
    ///
    /// Both overloads are here on purpose. Some of this codebase saves synchronously and some
    /// asynchronously, and instrumenting only one is precisely the sort of half-coverage that
    /// produced the bugs SyncCapture exists to end.
    ///
    /// The outbox entries are added before the save runs, so they are part of the same
    /// transaction as the shift, till or credit that caused them. A crash between the two is
    /// therefore not possible: either the branch took the money and has a record queued to
    /// report it, or neither happened.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        CaptureForSync();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        CaptureForSync();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void CaptureForSync()
    {
        var entries = SyncCapture.Collect(ChangeTracker);
        if (entries.Count > 0) SyncOutboxEntries.AddRange(entries);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // PostgreSQL extensions
        modelBuilder.HasPostgresExtension("uuid-ossp");
        modelBuilder.HasPostgresExtension("pgcrypto");

        // Hardening C.1: Optimistic Concurrency via PostgreSQL xmin
        modelBuilder.Entity<Session>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<Reservation>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<Bill>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<BillItem>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<Payment>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<CashRegister>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<CashTransaction>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<WalletTransaction>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<FoodOrder>().UseXminAsConcurrencyToken();
        modelBuilder.Entity<InventoryItem>().UseXminAsConcurrencyToken();

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
