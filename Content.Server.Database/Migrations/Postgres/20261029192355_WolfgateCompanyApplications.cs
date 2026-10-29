using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateCompanyApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "loadout",
                table: "wolfgate_company_rank",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "open_rank_id",
                table: "wolfgate_company",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_loadout",
                table: "wolfgate_company",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "wolfgate_company_application",
                columns: table => new
                {
                    wolfgate_company_application_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    rank_id = table.Column<int>(type: "integer", nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_application", x => x.wolfgate_company_application_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_application_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_application_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_application_company_id_profile_id",
                table: "wolfgate_company_application",
                columns: new[] { "company_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_application_profile_id",
                table: "wolfgate_company_application",
                column: "profile_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_company_application");

            migrationBuilder.DropColumn(
                name: "loadout",
                table: "wolfgate_company_rank");

            migrationBuilder.DropColumn(
                name: "open_rank_id",
                table: "wolfgate_company");

            migrationBuilder.DropColumn(
                name: "owner_loadout",
                table: "wolfgate_company");
        }
    }
}
