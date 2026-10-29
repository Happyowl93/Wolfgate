using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCompanyRankIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "icon",
                table: "wolfgate_company_rank",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "icon_version",
                table: "wolfgate_company_rank",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon",
                table: "wolfgate_company_rank");

            migrationBuilder.DropColumn(
                name: "icon_version",
                table: "wolfgate_company_rank");
        }
    }
}
