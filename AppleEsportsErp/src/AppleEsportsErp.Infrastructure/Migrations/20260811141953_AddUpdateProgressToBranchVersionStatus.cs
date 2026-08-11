using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdateProgressToBranchVersionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UpdateMessage",
                table: "BranchVersionStatuses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdateProgressPercent",
                table: "BranchVersionStatuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UpdateStage",
                table: "BranchVersionStatuses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdateStageChangedAt",
                table: "BranchVersionStatuses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdateMessage",
                table: "BranchVersionStatuses");

            migrationBuilder.DropColumn(
                name: "UpdateProgressPercent",
                table: "BranchVersionStatuses");

            migrationBuilder.DropColumn(
                name: "UpdateStage",
                table: "BranchVersionStatuses");

            migrationBuilder.DropColumn(
                name: "UpdateStageChangedAt",
                table: "BranchVersionStatuses");
        }
    }
}
