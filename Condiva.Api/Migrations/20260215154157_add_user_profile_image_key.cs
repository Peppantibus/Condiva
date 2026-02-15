using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Condiva.Api.Migrations
{
    /// <inheritdoc />
    public partial class add_user_profile_image_key : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProfileImageKey",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProfileImageKey",
                table: "Users");
        }
    }
}
