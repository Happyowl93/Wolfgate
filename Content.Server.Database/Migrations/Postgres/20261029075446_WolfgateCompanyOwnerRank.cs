using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateCompanyOwnerRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "owner_icon",
                table: "wolfgate_company",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "owner_icon_version",
                table: "wolfgate_company",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "owner_job",
                table: "wolfgate_company",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "owner_title",
                table: "wolfgate_company",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "owner_icon",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "owner_icon_version",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "owner_job",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "owner_title",
                table: "wolfgate_company");
        }
    }
}
