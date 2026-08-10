using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisterBusinessDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDay",
                table: "cash_register",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Backfill from when each drawer was opened. Without this every existing register
            // keeps the scaffolder's 0001-01-01 and can never match today: a branch with a
            // drawer currently open would be told none exists and would open a second one,
            // leaving the real money in a register the system had quietly abandoned.
            //
            // Uses the same rule as the application — the trading day runs 06:00-06:00 IST,
            // so a drawer opened before 06:00 belongs to the previous day.
            migrationBuilder.Sql(@"
                UPDATE cash_register
                SET ""BusinessDay"" = (
                    CASE
                        WHEN EXTRACT(HOUR FROM (""OpenedAt"" AT TIME ZONE 'Asia/Kolkata')) < 6
                            THEN (""OpenedAt"" AT TIME ZONE 'Asia/Kolkata')::date - INTERVAL '1 day'
                        ELSE (""OpenedAt"" AT TIME ZONE 'Asia/Kolkata')::date
                    END
                )::date
                -- Deliberately a range test, not equality against '0001-01-01'. Postgres
                -- stores DateOnly.MinValue as -infinity, so the equality version silently
                -- matched no rows at all and left every register on the sentinel date.
                WHERE ""BusinessDay"" < DATE '2000-01-01';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessDay",
                table: "cash_register");
        }
    }
}
