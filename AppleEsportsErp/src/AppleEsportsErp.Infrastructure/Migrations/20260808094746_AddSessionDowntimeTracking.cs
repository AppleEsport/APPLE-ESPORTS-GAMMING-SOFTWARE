using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionDowntimeTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InterruptedAt",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHeartbeatAt",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsTimeReview",
                table: "sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PausedSeconds",
                table: "sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The scaffolder also wanted employees.AadharDataUrl and employees.PhotoDataUrl
            // here: those properties exist on the model but were never given a migration.
            // They are already created at start-up by DbUpdater.UpdateSchema, which uses
            // "ADD COLUMN IF NOT EXISTS" and runs immediately after this. Adding them again
            // would throw "column already exists", and Program.cs logs migration failures
            // instead of rethrowing — so the session columns above would silently never be
            // created. Omitted deliberately; the model snapshot still records them.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterruptedAt",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "NeedsTimeReview",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "PausedSeconds",
                table: "sessions");

            // Employee columns are not dropped here — this migration never created them.
        }
    }
}
