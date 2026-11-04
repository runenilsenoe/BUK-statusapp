using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukStatus.Core.Groups.Migrations
{
    /// <inheritdoc />
    public partial class PotExtraContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtraDescriptionFilter",
                table: "PotSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraExcludedPersonIds",
                table: "PotSettings",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExtraFrom",
                table: "PotSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtraMode",
                table: "PotSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExtraOrgId",
                table: "PotSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExtraTo",
                table: "PotSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtraDescriptionFilter",
                table: "PotSettings");

            migrationBuilder.DropColumn(
                name: "ExtraExcludedPersonIds",
                table: "PotSettings");

            migrationBuilder.DropColumn(
                name: "ExtraFrom",
                table: "PotSettings");

            migrationBuilder.DropColumn(
                name: "ExtraMode",
                table: "PotSettings");

            migrationBuilder.DropColumn(
                name: "ExtraOrgId",
                table: "PotSettings");

            migrationBuilder.DropColumn(
                name: "ExtraTo",
                table: "PotSettings");
        }
    }
}
