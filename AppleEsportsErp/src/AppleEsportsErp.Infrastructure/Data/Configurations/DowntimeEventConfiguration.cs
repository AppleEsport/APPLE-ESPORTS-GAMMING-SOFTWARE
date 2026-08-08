using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppleEsportsErp.Domain.Entities;
using AppleEsportsErp.Domain.Enums;

namespace AppleEsportsErp.Infrastructure.Data.Configurations;

/// <summary>Power cuts and lost Head Office links, reported on the EOD.</summary>
public class DowntimeEventConfiguration : IEntityTypeConfiguration<DowntimeEvent>
{
    public void Configure(EntityTypeBuilder<DowntimeEvent> builder)
    {
        builder.ToTable("downtime_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()");

        // Stored as readable text ("power_or_restart") to match how SessionState and
        // ShiftStatus are persisted — these rows get read by eye during an audit.
        builder.Property(e => e.Kind).HasMaxLength(30).IsRequired()
            .HasConversion(
                v => v == DowntimeKind.PowerOrRestart ? "power_or_restart" : "internet_offline",
                v => v == "internet_offline" ? DowntimeKind.InternetOffline : DowntimeKind.PowerOrRestart);

        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.EndedAt).IsRequired();
        builder.Property(e => e.DurationSeconds).IsRequired();
        builder.Property(e => e.SessionsAffected).HasDefaultValue(0);
        builder.Property(e => e.BusinessDay).IsRequired();
        builder.Property(e => e.Notes).HasColumnType("text");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()");

        // The EOD asks "what outages hit this branch on this trading day" every time
        // a report is produced, so that pair is the index worth having.
        builder.HasIndex(e => new { e.BranchId, e.BusinessDay })
            .HasDatabaseName("idx_downtime_branch_day");

        builder.HasOne(e => e.Branch).WithMany()
            .HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.Cascade);
    }
}
