using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaydateRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaydateRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DogId = table.Column<int>(type: "integer", nullable: false),
                    OwnerId = table.Column<string>(type: "text", nullable: false),
                    LocationLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PreferredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SizePreference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EnergyLevel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaydateRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaydateRequests_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlaydateRequests_Dogs_DogId",
                        column: x => x.DogId,
                        principalTable: "Dogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaydateRequests_DogId",
                table: "PlaydateRequests",
                column: "DogId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaydateRequests_OwnerId",
                table: "PlaydateRequests",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaydateRequests");
        }
    }
}
