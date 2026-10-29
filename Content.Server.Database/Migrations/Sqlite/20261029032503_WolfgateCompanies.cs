using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_company",
                columns: table => new
                {
                    wolfgate_company_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    tag = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    colour = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    owner_profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    balance = table.Column<long>(type: "INTEGER", nullable: false),
                    cut = table.Column<int>(type: "INTEGER", nullable: false),
                    recruiting = table.Column<bool>(type: "INTEGER", nullable: false),
                    frozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company", x => x.wolfgate_company_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_ledger",
                columns: table => new
                {
                    wolfgate_company_ledger_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    company_id = table.Column<int>(type: "INTEGER", nullable: false),
                    time = table.Column<DateTime>(type: "TEXT", nullable: false),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    amount = table.Column<long>(type: "INTEGER", nullable: false),
                    balance_after = table.Column<long>(type: "INTEGER", nullable: false),
                    actor_profile_id = table.Column<int>(type: "INTEGER", nullable: true),
                    actor_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    note = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_ledger", x => x.wolfgate_company_ledger_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_ledger_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_member",
                columns: table => new
                {
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    company_id = table.Column<int>(type: "INTEGER", nullable: false),
                    rank_id = table.Column<int>(type: "INTEGER", nullable: true),
                    joined_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    unpaid_seconds = table.Column<int>(type: "INTEGER", nullable: false),
                    withdrawn_today = table.Column<int>(type: "INTEGER", nullable: false),
                    withdraw_day = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_member", x => x.profile_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_member_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_member_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_rank",
                columns: table => new
                {
                    wolfgate_company_rank_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    company_id = table.Column<int>(type: "INTEGER", nullable: false),
                    order = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    permissions = table.Column<int>(type: "INTEGER", nullable: false),
                    wage = table.Column<int>(type: "INTEGER", nullable: false),
                    withdraw_cap = table.Column<int>(type: "INTEGER", nullable: false),
                    base_job = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_rank", x => x.wolfgate_company_rank_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_rank_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_name",
                table: "wolfgate_company",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_tag",
                table: "wolfgate_company",
                column: "tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_ledger_company_id_time",
                table: "wolfgate_company_ledger",
                columns: new[] { "company_id", "time" });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_member_company_id",
                table: "wolfgate_company_member",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_rank_company_id",
                table: "wolfgate_company_rank",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_company_ledger");

            migrationBuilder.DropTable(
                name: "wolfgate_company_member");

            migrationBuilder.DropTable(
                name: "wolfgate_company_rank");

            migrationBuilder.DropTable(
                name: "wolfgate_company");
        }
    }
}
