using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukStatus.Core.Groups.Migrations
{
    /// <inheritdoc />
    public partial class PotAjourOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BukAjourPercentOverride",
                table: "PotSettings",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BukAjourPercentOverride",
                table: "PotSettings");
        }
    }
}
