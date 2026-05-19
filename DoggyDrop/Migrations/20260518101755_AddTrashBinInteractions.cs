using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTrashBinInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FullReports",
                table: "TrashBins",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReportedAt",
                table: "TrashBins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "TrashBins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MissingReports",
                table: "TrashBins",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "NotUsefulVotes",
                table: "TrashBins",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UsedCount",
                table: "TrashBins",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UsefulVotes",
                table: "TrashBins",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullReports",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "LastReportedAt",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "MissingReports",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "NotUsefulVotes",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "UsedCount",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "UsefulVotes",
                table: "TrashBins");
        }
    }
}
