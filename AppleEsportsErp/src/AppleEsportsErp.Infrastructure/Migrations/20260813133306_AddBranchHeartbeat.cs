using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_heartbeats",
                columns: table => new
                {
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BranchLocalTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OperatorsOnDuty = table.Column<string>(type: "jsonb", nullable: true),
                    OperatorsOnDutyCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveSessions = table.Column<int>(type: "integer", nullable: false),
                    PcsBusy = table.Column<int>(type: "integer", nullable: false),
                    PcsTotal = table.Column<int>(type: "integer", nullable: false),
                    DrawerExpected = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TakingsToday = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    UndeliveredRecords = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_heartbeats", x => x.BranchId);
                    table.ForeignKey(
                        name: "FK_branch_heartbeats_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_heartbeats");
        }
    }
}
