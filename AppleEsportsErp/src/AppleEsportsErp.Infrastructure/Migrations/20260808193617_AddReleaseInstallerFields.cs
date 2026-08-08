using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppleEsportsErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseInstallerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstallerFileName",
                table: "VersionInfos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstallerSha256",
                table: "VersionInfos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "InstallerSizeBytes",
                table: "VersionInfos",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstallerFileName",
                table: "VersionInfos");

            migrationBuilder.DropColumn(
                name: "InstallerSha256",
                table: "VersionInfos");

            migrationBuilder.DropColumn(
                name: "InstallerSizeBytes",
                table: "VersionInfos");
        }
    }
}
