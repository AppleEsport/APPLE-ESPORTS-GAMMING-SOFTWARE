using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeOperatorLinkAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // employees was never a proper EF migration - only DbUpdater.cs's raw-SQL patch
            // created it, and that runs AFTER migrations (see Program.cs). Every database that
            // had already run DbUpdater at least once - which was every database that existed
            // before this migration shipped - already had the table, so the ALTERs below just
            // worked. A genuinely fresh database has no such history: this migration is the
            // first thing to ever touch employees, "ALTER TABLE employees" fails outright
            // because it does not exist yet, the migration never gets recorded as applied, and
            // every restart retries the identical failure forever - the app can never finish
            // starting. Confirmed live on a brand-new install. IF NOT EXISTS makes this a
            // no-op everywhere the table already exists, so nothing changes for a database that
            // already passed this migration the normal way.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS employees (
    ""Id""                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    ""BranchId""                UUID NOT NULL REFERENCES branches(""Id"") ON DELETE RESTRICT,
    ""EmployeeNumber""          TEXT NOT NULL,
    ""FullName""                TEXT NOT NULL,
    ""Gender""                  TEXT,
    ""DateOfBirth""             DATE,
    ""Nationality""             TEXT DEFAULT 'Indian',
    ""MaritalStatus""           TEXT,
    ""PermanentAddress""        TEXT,
    ""CurrentAddress""          TEXT,
    ""Phone""                   TEXT,
    ""Email""                   TEXT,
    ""EmergencyName""           TEXT,
    ""EmergencyRelationship""   TEXT,
    ""EmergencyPhone""          TEXT,
    ""EmergencyEmail""          TEXT,
    ""EmergencyAddress""        TEXT,
    ""PositionTitle""           TEXT,
    ""Department""              TEXT,
    ""Supervisor""              TEXT,
    ""StartDate""               DATE,
    ""BankName""                TEXT,
    ""AccountNumber""           TEXT,
    ""AccountHolderName""       TEXT,
    ""BankBranch""              TEXT,
    ""RefName""                 TEXT,
    ""RefRelationship""         TEXT,
    ""RefPhone""                TEXT,
    ""RefAddress""              TEXT,
    ""PhotoDataUrl""            TEXT,
    ""AadharDataUrl""           TEXT,
    ""Status""                  TEXT NOT NULL DEFAULT 'Active',
    ""SubmittedBy""             UUID REFERENCES operators(""Id"") ON DELETE SET NULL,
    ""CreatedAt""               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ""UpdatedAt""               TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'employees_employeenumber_unique'
  ) THEN
    ALTER TABLE employees ADD CONSTRAINT employees_employeenumber_unique UNIQUE (""EmployeeNumber"");
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_employees_branch_id ON employees(""BranchId"");
CREATE INDEX IF NOT EXISTS idx_employees_employee_number ON employees(""EmployeeNumber"");
CREATE INDEX IF NOT EXISTS idx_employees_status ON employees(""Status"");
");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OperatorId",
                table: "employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_employees_OperatorId",
                table: "employees",
                column: "OperatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_employees_operators_OperatorId",
                table: "employees",
                column: "OperatorId",
                principalTable: "operators",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_employees_operators_OperatorId",
                table: "employees");

            migrationBuilder.DropIndex(
                name: "IX_employees_OperatorId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "OperatorId",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "employees");
        }
    }
}
