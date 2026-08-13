using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using AppleEsportsErp.Domain.Entities;

namespace AppleEsportsErp.Infrastructure.Data.Configurations;

public class BranchCommandConfiguration : IEntityTypeConfiguration<BranchCommand>
{
    public void Configure(EntityTypeBuilder<BranchCommand> builder)
    {
        builder.ToTable("branch_commands");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnType("uuid").HasDefaultValueSql("uuid_generate_v4()");
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(e => e.ResultMessage).HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.DeliveredAt).HasColumnType("timestamp with time zone");
        builder.Property(e => e.ConfirmedAt).HasColumnType("timestamp with time zone");

        // The one query the branch runs on every heartbeat: my pending work, oldest first.
        builder.HasIndex(e => new { e.BranchId, e.Status }).HasDatabaseName("idx_branch_commands_pending");
    }
}
