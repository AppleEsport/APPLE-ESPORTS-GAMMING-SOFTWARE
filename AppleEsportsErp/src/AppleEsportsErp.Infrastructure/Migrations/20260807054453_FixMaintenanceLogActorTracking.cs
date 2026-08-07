using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixMaintenanceLogActorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MaintenanceLogs_operators_OperatorId",
                table: "MaintenanceLogs");

            migrationBuilder.DropIndex(
                name: "IX_MaintenanceLogs_OperatorId",
                table: "MaintenanceLogs");

            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "MaintenanceLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MarkedByName",
                table: "MaintenanceLogs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResolvedByName",
                table: "MaintenanceLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorRole",
                table: "MaintenanceLogs");

            migrationBuilder.DropColumn(
                name: "MarkedByName",
                table: "MaintenanceLogs");

            migrationBuilder.DropColumn(
                name: "ResolvedByName",
                table: "MaintenanceLogs");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceLogs_OperatorId",
                table: "MaintenanceLogs",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaintenanceLogs_operators_OperatorId",
                table: "MaintenanceLogs",
                column: "OperatorId",
                principalTable: "operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
