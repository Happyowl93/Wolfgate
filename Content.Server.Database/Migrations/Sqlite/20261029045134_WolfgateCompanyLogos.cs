using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCompanyLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "art_locked",
                table: "wolfgate_company",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "logo",
                table: "wolfgate_company",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "logo_version",
                table: "wolfgate_company",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "art_locked",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "logo",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "logo_version",
                table: "wolfgate_company");
        }
    }
}
