using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddPlannedWalks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlannedWalkId",
                table: "Walks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlannedWalks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    DogId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AreaKey = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AreaName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    TargetDistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedDistanceKm = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    IncludeBins = table.Column<bool>(type: "boolean", nullable: false),
                    IncludePark = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeWater = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeDogFriendly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedWalks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedWalks_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlannedWalks_Dogs_DogId",
                        column: x => x.DogId,
                        principalTable: "Dogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlannedWalkStops",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlannedWalkId = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Reason = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedWalkStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlannedWalkStops_PlannedWalks_PlannedWalkId",
                        column: x => x.PlannedWalkId,
                        principalTable: "PlannedWalks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Walks_PlannedWalkId",
                table: "Walks",
                column: "PlannedWalkId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedWalks_DogId",
                table: "PlannedWalks",
                column: "DogId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedWalks_OwnerId",
                table: "PlannedWalks",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlannedWalkStops_PlannedWalkId_Order",
                table: "PlannedWalkStops",
                columns: new[] { "PlannedWalkId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_Walks_PlannedWalks_PlannedWalkId",
                table: "Walks",
                column: "PlannedWalkId",
                principalTable: "PlannedWalks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Walks_PlannedWalks_PlannedWalkId",
                table: "Walks");

            migrationBuilder.DropTable(
                name: "PlannedWalkStops");

            migrationBuilder.DropTable(
                name: "PlannedWalks");

            migrationBuilder.DropIndex(
                name: "IX_Walks_PlannedWalkId",
                table: "Walks");

            migrationBuilder.DropColumn(
                name: "PlannedWalkId",
                table: "Walks");
        }
    }
}
