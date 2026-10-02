using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CloseStaleCitylightShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-3.1.69 shifts that never closed, at Citylight-144Hz only (BranchId below) -
            // Adajan-240Hz is deliberately left untouched, it is not affected.
            //
            // Scoped to a shift that is STRICTLY SUPERSEDED: it only closes a row when the same
            // operator already has a later shift (of any status). That is the one fact that
            // makes closing it safe without guessing at a real LogoutTime - an operator can only
            // ever truly be "in" their single most recent shift, so anything with a newer
            // sibling is definitionally stale, and the one shift each operator is actually
            // working right now - having no newer sibling - is never touched by this migration,
            // no matter how old it is.
            migrationBuilder.Sql(@"
                UPDATE shifts
                SET ""Status"" = 'completed',
                    ""LogoutTime"" = NOW()
                WHERE ""BranchId"" = '7a280afd-b21d-47e5-85a5-c26c6aa82150'
                  AND ""Status"" = 'active'
                  AND EXISTS (
                      SELECT 1 FROM shifts newer
                      WHERE newer.""OperatorId"" = shifts.""OperatorId""
                        AND newer.""LoginTime"" > shifts.""LoginTime""
                  );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible - the real LogoutTime these shifts should have had was never
            // recorded, so there is nothing truthful to restore. Same as the cash register
            // cleanup this mirrors: the data fix itself has no Down, only this migration's own
            // record of having run does.
        }
    }
}
