using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkStopCompletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WalkStopCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WalkId = table.Column<int>(type: "integer", nullable: false),
                    PlannedWalkStopId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkStopCompletions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalkStopCompletions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalkStopCompletions_PlannedWalkStops_PlannedWalkStopId",
                        column: x => x.PlannedWalkStopId,
                        principalTable: "PlannedWalkStops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalkStopCompletions_Walks_WalkId",
                        column: x => x.WalkId,
                        principalTable: "Walks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalkStopCompletions_PlannedWalkStopId",
                table: "WalkStopCompletions",
                column: "PlannedWalkStopId");

            migrationBuilder.CreateIndex(
                name: "IX_WalkStopCompletions_UserId",
                table: "WalkStopCompletions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WalkStopCompletions_WalkId_PlannedWalkStopId",
                table: "WalkStopCompletions",
                columns: new[] { "WalkId", "PlannedWalkStopId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalkStopCompletions");
        }
    }
}
