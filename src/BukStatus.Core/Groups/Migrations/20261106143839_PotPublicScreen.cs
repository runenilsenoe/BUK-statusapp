using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukStatus.Core.Groups.Migrations
{
    /// <inheritdoc />
    public partial class PotPublicScreen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PublicScreenEnabled",
                table: "PotSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicScreenEnabled",
                table: "PotSettings");
        }
    }
}
