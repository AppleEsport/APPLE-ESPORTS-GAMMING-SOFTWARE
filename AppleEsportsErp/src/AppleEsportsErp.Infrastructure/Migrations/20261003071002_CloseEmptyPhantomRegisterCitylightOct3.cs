using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloseEmptyPhantomRegisterCitylightOct3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One specific register, Citylight-144Hz, opened by the kiosk self-service flow on
            // Oct 3 before the fix for that (3.1.72) had reached this branch. Still at ₹0
            // Expected/Opening - no real transactions ever landed on it - so closing it loses
            // nothing. Scoped to this exact Id on purpose: a blanket "close every system_admin
            // register" rule would also catch one mid-transaction on a branch that hasn't hit
            // this specific morning's bug, which is not what this is for.
            migrationBuilder.Sql(@"
                UPDATE cash_register
                SET ""Status"" = 'closed',
                    ""ClosedAt"" = NOW()
                WHERE ""Id"" = '80244442-c98a-47b3-bfec-c57ca0239608'
                  AND ""Status"" = 'open'
                  AND ""ExpectedDrawerCash"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not meaningfully reversible, same as the other one-off cleanups tonight - there is
            // nothing truthful to restore a phantom register to.
        }
    }
}
