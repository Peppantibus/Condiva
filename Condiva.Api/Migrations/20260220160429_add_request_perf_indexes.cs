using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_request_perf_indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Requests_CommunityId",
                table: "Requests");

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResponseStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ResponseBody = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseContentType = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseLocation = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CommunityId_RequesterUserId_Status_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "CommunityId", "RequesterUserId", "Status", "CreatedAt", "Id" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CommunityId_Status_CreatedAt_Id",
                table: "Requests",
                columns: new[] { "CommunityId", "Status", "CreatedAt", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CommunityId_Status_NeededTo",
                table: "Requests",
                columns: new[] { "CommunityId", "Status", "NeededTo" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ActorUserId_Method_Path_IdempotencyKey",
                table: "IdempotencyRecords",
                columns: new[] { "ActorUserId", "Method", "Path", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CommunityId_RequesterUserId_Status_CreatedAt_Id",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CommunityId_Status_CreatedAt_Id",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Requests_CommunityId_Status_NeededTo",
                table: "Requests");

            migrationBuilder.CreateIndex(
                name: "IX_Requests_CommunityId",
                table: "Requests",
                column: "CommunityId");
        }
    }
}
