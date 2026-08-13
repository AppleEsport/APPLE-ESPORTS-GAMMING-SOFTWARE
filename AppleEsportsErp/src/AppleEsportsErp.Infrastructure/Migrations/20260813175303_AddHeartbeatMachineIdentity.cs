using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHeartbeatMachineIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConflictingMachine",
                table: "branch_heartbeats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConflictingMachineSeenAt",
                table: "branch_heartbeats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedByMachine",
                table: "branch_heartbeats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictingMachine",
                table: "branch_heartbeats");

            migrationBuilder.DropColumn(
                name: "ConflictingMachineSeenAt",
                table: "branch_heartbeats");

            migrationBuilder.DropColumn(
                name: "ReportedByMachine",
                table: "branch_heartbeats");
        }
    }
}
