using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationsImprovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationDispatchStates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    LastProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastProcessedEventId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatchStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_EventId_Type_RecipientUserId",
                table: "Notifications",
                columns: new[] { "EventId", "Type", "RecipientUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationDispatchStates");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_EventId_Type_RecipientUserId",
                table: "Notifications");
        }
    }
}
