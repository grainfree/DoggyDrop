using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaydateInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaydateInterests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaydateRequestId = table.Column<int>(type: "integer", nullable: false),
                    DogId = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaydateInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaydateInterests_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaydateInterests_Dogs_DogId",
                        column: x => x.DogId,
                        principalTable: "Dogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaydateInterests_PlaydateRequests_PlaydateRequestId",
                        column: x => x.PlaydateRequestId,
                        principalTable: "PlaydateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaydateInterests_DogId",
                table: "PlaydateInterests",
                column: "DogId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaydateInterests_OwnerId",
                table: "PlaydateInterests",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaydateInterests_PlaydateRequestId",
                table: "PlaydateInterests",
                column: "PlaydateRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaydateInterests");
        }
    }
}
