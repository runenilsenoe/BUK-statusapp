using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukStatus.Core.Groups.Migrations
{
    /// <inheritdoc />
    public partial class SamvirkSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SamvirkImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SamvirkImports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SamvirkImports_Hash_SnapshotDate",
                table: "SamvirkImports",
                columns: new[] { "Hash", "SnapshotDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SamvirkImports");
        }
    }
}
