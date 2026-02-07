using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_external_auth_logins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalAuthLogins",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalAuthLogins", x => new { x.Provider, x.Subject });
                    table.ForeignKey(
                        name: "FK_ExternalAuthLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalAuthLogins_UserId",
                table: "ExternalAuthLogins",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalAuthLogins");
        }
    }
}
