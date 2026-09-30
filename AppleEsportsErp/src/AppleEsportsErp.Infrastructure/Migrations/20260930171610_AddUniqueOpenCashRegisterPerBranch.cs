using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueOpenCashRegisterPerBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Real production data already has more than one non-closed register for the same
            // branch in places (confirmed at Citylight, 30 Sep 2026) - the unique index below
            // would refuse to create at all until that's fixed. Auto-close only the ones with
            // zero cash transactions attached (a genuine duplicate that never rang anything up,
            // safe to fold away with no loss). Keep the most recently opened per branch. A
            // duplicate that DOES hold real transactions is deliberately left alone - this
            // migration then fails loudly instead of guessing which register holds real money.
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT cr.""Id"",
                           ROW_NUMBER() OVER (PARTITION BY cr.""BranchId"" ORDER BY cr.""OpenedAt"" DESC) AS rn,
                           (SELECT COUNT(*) FROM cash_transactions ct WHERE ct.""CashRegisterId"" = cr.""Id"") AS txn_count
                    FROM cash_register cr
                    WHERE cr.""Status"" <> 'closed'
                )
                UPDATE cash_register
                SET ""Status"" = 'closed',
                    ""ClosedAt"" = NOW(),
                    ""MismatchReason"" = COALESCE(""MismatchReason"" || ' | ', '') ||
                        'Auto-closed by migration AddUniqueOpenCashRegisterPerBranch: duplicate register with zero transactions, superseded by a more recently opened one for the same branch.'
                FROM ranked
                WHERE cash_register.""Id"" = ranked.""Id"" AND ranked.rn > 1 AND ranked.txn_count = 0;
            ");

            // A system_admin_* register is never legitimate any more - the only code path that
            // ever created one (ControllerExtensions.GetShiftIdAsync) no longer does. Close out
            // any that are still sitting open with zero transactions, whether or not they were
            // caught as a duplicate above (a branch can have exactly one open register and still
            // have it be the wrong one, if that one happens to be the phantom).
            migrationBuilder.Sql(@"
                UPDATE cash_register
                SET ""Status"" = 'closed',
                    ""ClosedAt"" = NOW(),
                    ""MismatchReason"" = COALESCE(""MismatchReason"" || ' | ', '') ||
                        'Auto-closed by migration AddUniqueOpenCashRegisterPerBranch: system_admin registers are never legitimate and are always closed automatically.'
                WHERE ""Status"" <> 'closed'
                  AND (SELECT COUNT(*) FROM cash_transactions ct WHERE ct.""CashRegisterId"" = cash_register.""Id"") = 0
                  AND ""OperatorId"" IN (
                      SELECT ""Id"" FROM operators WHERE ""Username"" LIKE 'system_admin_%'
                  );
            ");

            migrationBuilder.DropIndex(
                name: "idx_cash_register_branch",
                table: "cash_register");

            migrationBuilder.CreateIndex(
                name: "ux_cash_register_one_open_per_branch",
                table: "cash_register",
                column: "BranchId",
                unique: true,
                filter: "\"Status\" <> 'closed'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_cash_register_one_open_per_branch",
                table: "cash_register");

            migrationBuilder.CreateIndex(
                name: "idx_cash_register_branch",
                table: "cash_register",
                column: "BranchId");
        }
    }
}
