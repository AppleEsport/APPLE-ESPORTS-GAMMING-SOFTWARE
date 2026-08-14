using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An earlier attempt at this feature created a branch_commands table with a
            // different shape - Type/PayloadJson where this uses CommandType/Payload - and was
            // then reverted in code. The revert removed the code but not the table, so Head
            // Office is left with an orphan no code refers to, and CreateTable below would
            // fail against it.
            //
            // Dropped rather than migrated because there is genuinely nothing to preserve: the
            // table is empty on every database that has it, and the code that would have
            // written to it no longer exists. Doing it here rather than by hand on the server
            // means any other database in the same state - a restored backup, a second Head
            // Office - is fixed the same way without anyone remembering to.
            //
            // IF EXISTS keeps this a no-op everywhere else, which is every branch database.
            migrationBuilder.Sql("DROP TABLE IF EXISTS branch_commands CASCADE;");

            migrationBuilder.CreateTable(
                name: "branch_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommandType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_branch_commands_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_branch_commands_branch_status",
                table: "branch_commands",
                columns: new[] { "BranchId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_commands");
        }
    }
}
