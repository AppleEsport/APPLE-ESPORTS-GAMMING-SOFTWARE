using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Infrastructure.Data.Configurations;

/// <summary>Events received at Head Office from branches, stored as they arrived.</summary>
public class SyncInboxEntryConfiguration : IEntityTypeConfiguration<SyncInboxEntry>
{
    public void Configure(EntityTypeBuilder<SyncInboxEntry> builder)
    {
        builder.ToTable("sync_inbox_entries");

        // The branch's own outbox id, reused as the key on purpose: if a branch re-sends a
        // batch after a timeout it never learned the outcome of, the insert collides here
        // rather than recording the same payment twice.
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AggregateType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.EventType).HasMaxLength(60).IsRequired();
        builder.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired();
        builder.Property(e => e.Applied).HasDefaultValue(false);
        builder.Property(e => e.ApplyError).HasColumnType("text");

        builder.HasIndex(e => new { e.BranchId, e.OccurredAt })
            .HasDatabaseName("idx_sync_inbox_branch_time");

        // Finding what has arrived but not yet been folded into reports — the queue a
        // retry pass works through once a branch is properly provisioned.
        builder.HasIndex(e => e.Applied)
            .HasDatabaseName("idx_sync_inbox_applied")
            .HasFilter("\"Applied\" = false");

        // No FK to branches on purpose. A branch can legitimately report in before Head
        // Office has a row for it, and refusing the data would be the same silent loss
        // this table exists to prevent.
        builder.Ignore(e => e.Branch);
    }
}
