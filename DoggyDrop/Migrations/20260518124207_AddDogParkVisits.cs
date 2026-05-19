using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddDogParkVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DogParkVisits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DogId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ParkName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Address = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PlaceKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    VisitedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DogParkVisits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DogParkVisits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DogParkVisits_Dogs_DogId",
                        column: x => x.DogId,
                        principalTable: "Dogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DogParkVisits_DogId_PlaceKey_VisitedAt",
                table: "DogParkVisits",
                columns: new[] { "DogId", "PlaceKey", "VisitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DogParkVisits_UserId",
                table: "DogParkVisits",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DogParkVisits");
        }
    }
}
