using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    // Note: the cash_transactions.ActualCashCollected/CashReceived/ChangeReturned columns
    // (added to CashTransaction.cs in a prior commit) already exist on the live DB — they were
    // applied out-of-band and never recorded in __EFMigrationsHistory. This migration only adds
    // FoodOrder.BillId; it deliberately omits those three AddColumn/DropColumn calls to avoid a
    // "column already exists" collision. The model snapshot still reflects them correctly.
    public partial class AddFoodOrderBillIdAndCashTransactionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BillId",
                table: "food_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_food_orders_BillId",
                table: "food_orders",
                column: "BillId");

            migrationBuilder.AddForeignKey(
                name: "FK_food_orders_bills_BillId",
                table: "food_orders",
                column: "BillId",
                principalTable: "bills",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_food_orders_bills_BillId",
                table: "food_orders");

            migrationBuilder.DropIndex(
                name: "IX_food_orders_BillId",
                table: "food_orders");

            migrationBuilder.DropColumn(
                name: "BillId",
                table: "food_orders");
        }
    }
}
