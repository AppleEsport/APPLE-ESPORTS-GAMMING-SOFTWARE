using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletTransactionShiftId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShiftId",
                table: "wallet_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_wallet_transactions_ShiftId",
                table: "wallet_transactions",
                column: "ShiftId");

            migrationBuilder.AddForeignKey(
                name: "FK_wallet_transactions_shifts_ShiftId",
                table: "wallet_transactions",
                column: "ShiftId",
                principalTable: "shifts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_wallet_transactions_shifts_ShiftId",
                table: "wallet_transactions");

            migrationBuilder.DropIndex(
                name: "IX_wallet_transactions_ShiftId",
                table: "wallet_transactions");

            migrationBuilder.DropColumn(
                name: "ShiftId",
                table: "wallet_transactions");
        }
    }
}
