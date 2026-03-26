using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_content_moderation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentModerationMode",
                table: "Communities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CommunityBannedTerms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CommunityId = table.Column<string>(type: "TEXT", nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedTerm = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunityBannedTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommunityBannedTerms_Communities_CommunityId",
                        column: x => x.CommunityId,
                        principalTable: "Communities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommunityBannedTerms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBannedTerms_CommunityId_IsActive",
                table: "CommunityBannedTerms",
                columns: new[] { "CommunityId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBannedTerms_CommunityId_NormalizedTerm",
                table: "CommunityBannedTerms",
                columns: new[] { "CommunityId", "NormalizedTerm" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommunityBannedTerms_CreatedByUserId",
                table: "CommunityBannedTerms",
                column: "CreatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommunityBannedTerms");

            migrationBuilder.DropColumn(
                name: "ContentModerationMode",
                table: "Communities");
        }
    }
}
