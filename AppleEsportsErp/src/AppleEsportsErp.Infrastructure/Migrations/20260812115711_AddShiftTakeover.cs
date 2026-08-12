using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShiftTakeover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByOperatorId",
                table: "shifts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CountedByOperatorId",
                table: "cash_register",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shift_handovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutgoingShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutgoingOperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CountedByOperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncomingShiftId = table.Column<Guid>(type: "uuid", nullable: true),
                    CashRegisterId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    CountedCash = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    CashDifference = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    StockDifferences = table.Column<string>(type: "jsonb", nullable: true),
                    UnattendedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "awaiting_reason"),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_handovers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shift_handovers_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_handovers_cash_register_CashRegisterId",
                        column: x => x.CashRegisterId,
                        principalTable: "cash_register",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_handovers_operators_CountedByOperatorId",
                        column: x => x.CountedByOperatorId,
                        principalTable: "operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_handovers_operators_OutgoingOperatorId",
                        column: x => x.OutgoingOperatorId,
                        principalTable: "operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_handovers_shifts_IncomingShiftId",
                        column: x => x.IncomingShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_shift_handovers_shifts_OutgoingShiftId",
                        column: x => x.OutgoingShiftId,
                        principalTable: "shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_shift_handover_branch",
                table: "shift_handovers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "idx_shift_handover_counted_by_status",
                table: "shift_handovers",
                columns: new[] { "CountedByOperatorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_shift_handover_outgoing",
                table: "shift_handovers",
                column: "OutgoingShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_handovers_CashRegisterId",
                table: "shift_handovers",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_handovers_IncomingShiftId",
                table: "shift_handovers",
                column: "IncomingShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_shift_handovers_OutgoingOperatorId",
                table: "shift_handovers",
                column: "OutgoingOperatorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shift_handovers");

            migrationBuilder.DropColumn(
                name: "ClosedByOperatorId",
                table: "shifts");

            migrationBuilder.DropColumn(
                name: "CountedByOperatorId",
                table: "cash_register");
        }
    }
}
