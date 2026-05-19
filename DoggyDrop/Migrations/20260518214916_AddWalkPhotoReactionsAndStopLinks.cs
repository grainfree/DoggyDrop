using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkPhotoReactionsAndStopLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlannedWalkStopId",
                table: "WalkPhotos",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WalkPhotoReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalkPhotoId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkPhotoReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalkPhotoReactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalkPhotoReactions_WalkPhotos_WalkPhotoId",
                        column: x => x.WalkPhotoId,
                        principalTable: "WalkPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalkPhotos_PlannedWalkStopId",
                table: "WalkPhotos",
                column: "PlannedWalkStopId");

            migrationBuilder.CreateIndex(
                name: "IX_WalkPhotoReactions_UserId",
                table: "WalkPhotoReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalkPhotoReactions_WalkPhotoId_UserId",
                table: "WalkPhotoReactions",
                columns: new[] { "WalkPhotoId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WalkPhotos_PlannedWalkStops_PlannedWalkStopId",
                table: "WalkPhotos",
                column: "PlannedWalkStopId",
                principalTable: "PlannedWalkStops",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WalkPhotos_PlannedWalkStops_PlannedWalkStopId",
                table: "WalkPhotos");

            migrationBuilder.DropTable(
                name: "WalkPhotoReactions");

            migrationBuilder.DropIndex(
                name: "IX_WalkPhotos_PlannedWalkStopId",
                table: "WalkPhotos");

            migrationBuilder.DropColumn(
                name: "PlannedWalkStopId",
                table: "WalkPhotos");
        }
    }
}
