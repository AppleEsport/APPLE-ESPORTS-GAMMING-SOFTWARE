using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogSuccessFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generator's own default was false, which would have backfilled every
            // existing row in the company as a failure - years of ordinary, successful
            // sessions and payments suddenly marked red. True is correct for history: only
            // login ever had its own distinct failure codes before this column existed, and
            // everything else that is in this table happened, or it would not have a row.
            migrationBuilder.AddColumn<bool>(
                name: "Success",
                table: "audit_logs",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Success",
                table: "audit_logs");
        }
    }
}
