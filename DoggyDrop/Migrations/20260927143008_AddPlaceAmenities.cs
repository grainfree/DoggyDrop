using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceAmenities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AmenitiesSourceUrl",
                table: "Places",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AmenitiesVerifiedAt",
                table: "Places",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlaceAmenities",
                columns: table => new
                {
                    PlaceId = table.Column<int>(type: "integer", nullable: false),
                    AmenityType = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceAmenities", x => new { x.PlaceId, x.AmenityType });
                    table.ForeignKey(
                        name: "FK_PlaceAmenities_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceAmenities");

            migrationBuilder.DropColumn(
                name: "AmenitiesSourceUrl",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "AmenitiesVerifiedAt",
                table: "Places");
        }
    }
}
