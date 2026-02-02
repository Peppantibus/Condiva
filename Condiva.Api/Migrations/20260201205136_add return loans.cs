using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class addreturnloans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnConfirmedAt",
                table: "Loans",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnRequestedAt",
                table: "Loans",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnConfirmedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnRequestedAt",
                table: "Loans");
        }
    }
}
