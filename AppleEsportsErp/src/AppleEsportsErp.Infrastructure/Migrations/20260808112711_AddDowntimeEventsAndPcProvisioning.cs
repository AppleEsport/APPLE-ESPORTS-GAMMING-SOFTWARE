using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDowntimeEventsAndPcProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MachineId",
                table: "pcs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineToken",
                table: "pcs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvisionedAt",
                table: "pcs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "downtime_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    SessionsAffected = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    BusinessDay = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_downtime_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_downtime_events_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_pcs_machine_unique",
                table: "pcs",
                column: "MachineId",
                unique: true,
                filter: "\"MachineId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_downtime_branch_day",
                table: "downtime_events",
                columns: new[] { "BranchId", "BusinessDay" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "downtime_events");

            migrationBuilder.DropIndex(
                name: "idx_pcs_machine_unique",
                table: "pcs");

            migrationBuilder.DropColumn(
                name: "MachineId",
                table: "pcs");

            migrationBuilder.DropColumn(
                name: "MachineToken",
                table: "pcs");

            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "pcs");
        }
    }
}
