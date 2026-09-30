using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisterCoverAmountAndNextDayFloat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CoverAmount",
                table: "cash_register",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NextDayFloatReason",
                table: "cash_register",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NextDayOpeningBalance",
                table: "cash_register",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverAmount",
                table: "cash_register");

            migrationBuilder.DropColumn(
                name: "NextDayFloatReason",
                table: "cash_register");

            migrationBuilder.DropColumn(
                name: "NextDayOpeningBalance",
                table: "cash_register");
        }
    }
}
