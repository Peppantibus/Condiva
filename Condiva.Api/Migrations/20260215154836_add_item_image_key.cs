using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_item_image_key : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageKey",
                table: "Items",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageKey",
                table: "Items");
        }
    }
}
