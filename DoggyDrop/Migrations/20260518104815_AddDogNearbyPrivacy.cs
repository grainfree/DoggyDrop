using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddDogNearbyPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "LastKnownLatitude",
                table: "Dogs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LastKnownLongitude",
                table: "Dogs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLocationUpdatedAt",
                table: "Dogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NearbyVisibility",
                table: "Dogs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastKnownLatitude",
                table: "Dogs");

            migrationBuilder.DropColumn(
                name: "LastKnownLongitude",
                table: "Dogs");

            migrationBuilder.DropColumn(
                name: "LastLocationUpdatedAt",
                table: "Dogs");

            migrationBuilder.DropColumn(
                name: "NearbyVisibility",
                table: "Dogs");
        }
    }
}
