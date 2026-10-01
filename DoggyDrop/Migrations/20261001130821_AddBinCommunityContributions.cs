using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddBinCommunityContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRejected",
                table: "TrashBins",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRetired",
                table: "TrashBins",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "TrashBins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BinContributions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BinId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProposedLatitude = table.Column<double>(type: "double precision", nullable: true),
                    ProposedLongitude = table.Column<double>(type: "double precision", nullable: true),
                    PossibleDuplicateBinId = table.Column<int>(type: "integer", nullable: true),
                    ProposedPhotoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SubmittedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    ReviewedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    BinSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BinContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BinContributions_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BinContributions_AspNetUsers_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BinContributions_TrashBins_BinId",
                        column: x => x.BinId,
                        principalTable: "TrashBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BinContributions_BinId",
                table: "BinContributions",
                column: "BinId");

            migrationBuilder.CreateIndex(
                name: "IX_BinContributions_ReviewedByUserId",
                table: "BinContributions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BinContributions_Status_CreatedAt",
                table: "BinContributions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BinContributions_SubmittedByUserId_BinId_Type_Reason",
                table: "BinContributions",
                columns: new[] { "SubmittedByUserId", "BinId", "Type", "Reason" },
                unique: true,
                filter: "\"Status\" = 1 AND \"Type\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BinContributions_SubmittedByUserId_RequestId",
                table: "BinContributions",
                columns: new[] { "SubmittedByUserId", "RequestId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BinContributions");

            migrationBuilder.DropColumn(
                name: "IsRejected",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "IsRetired",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "TrashBins");
        }
    }
}
