using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateCompanyHangar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "launch_fee",
                table: "wolfgate_company",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "wolfgate_company_licence",
                columns: table => new
                {
                    wolfgate_company_licence_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    vessel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price_paid = table.Column<long>(type: "bigint", nullable: false),
                    bought_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_licence", x => x.wolfgate_company_licence_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_licence_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_licence_company_id",
                table: "wolfgate_company_licence",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_company_licence");

            migrationBuilder.DropColumn(
                name: "launch_fee",
                table: "wolfgate_company");
        }
    }
}
