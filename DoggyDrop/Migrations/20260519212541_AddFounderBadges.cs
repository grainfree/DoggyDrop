using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddFounderBadges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FounderBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AreaKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AreaName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BadgeType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TrashBinId = table.Column<int>(type: "integer", nullable: true),
                    UnlockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FounderBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FounderBadges_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FounderBadges_TrashBins_TrashBinId",
                        column: x => x.TrashBinId,
                        principalTable: "TrashBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FounderBadges_AreaKey_BadgeType",
                table: "FounderBadges",
                columns: new[] { "AreaKey", "BadgeType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FounderBadges_TrashBinId",
                table: "FounderBadges",
                column: "TrashBinId");

            migrationBuilder.CreateIndex(
                name: "IX_FounderBadges_UserId_UnlockedAt",
                table: "FounderBadges",
                columns: new[] { "UserId", "UnlockedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FounderBadges");
        }
    }
}
