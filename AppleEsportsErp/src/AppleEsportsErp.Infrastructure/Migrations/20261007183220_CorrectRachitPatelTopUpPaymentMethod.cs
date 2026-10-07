using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CorrectRachitPatelTopUpPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One specific top-up, Adajan-240Hz, 7 Oct 2026 - the operator entered it as cash
            // by mistake; it was actually paid online. The Rs 20,000 real payment moves from
            // Cash to Online; Amount (22,000) and the Rs 2,000 bonus already earned are
            // untouched, since the total credited to the member never changes, only which
            // method the real money came in through.
            migrationBuilder.Sql(@"
                UPDATE wallet_transactions
                SET ""CashAmount"" = 0,
                    ""OnlineAmount"" = 20000,
                    ""PaymentType"" = 'Online'
                WHERE ""Id"" = 'bf5526d5-9798-4a1d-9679-a5bc7343f83d'
                  AND ""CashAmount"" = 20000
                  AND ""OnlineAmount"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE wallet_transactions
                SET ""CashAmount"" = 20000,
                    ""OnlineAmount"" = 0,
                    ""PaymentType"" = 'Cash'
                WHERE ""Id"" = 'bf5526d5-9798-4a1d-9679-a5bc7343f83d'
                  AND ""CashAmount"" = 0
                  AND ""OnlineAmount"" = 20000;
            ");
        }
    }
}
