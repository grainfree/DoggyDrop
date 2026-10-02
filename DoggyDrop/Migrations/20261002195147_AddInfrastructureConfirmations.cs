using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddInfrastructureConfirmations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceVersion",
                table: "WaterPoints",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceVersion",
                table: "TrashBins",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "InfrastructureConfirmations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    TrashBinId = table.Column<int>(type: "integer", nullable: true),
                    WaterPointId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceVersion = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfrastructureConfirmations", x => x.Id);
                    table.CheckConstraint("CK_Confirmation_Target", "(\"Type\" = 1 AND \"TrashBinId\" IS NOT NULL AND \"WaterPointId\" IS NULL) OR (\"Type\" = 2 AND \"WaterPointId\" IS NOT NULL AND \"TrashBinId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_InfrastructureConfirmations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InfrastructureConfirmations_TrashBins_TrashBinId",
                        column: x => x.TrashBinId,
                        principalTable: "TrashBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InfrastructureConfirmations_WaterPoints_WaterPointId",
                        column: x => x.WaterPointId,
                        principalTable: "WaterPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureConfirmations_TrashBinId_EvidenceVersion_Crea~",
                table: "InfrastructureConfirmations",
                columns: new[] { "TrashBinId", "EvidenceVersion", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureConfirmations_UserId_TrashBinId_CreatedAt",
                table: "InfrastructureConfirmations",
                columns: new[] { "UserId", "TrashBinId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureConfirmations_UserId_WaterPointId_CreatedAt",
                table: "InfrastructureConfirmations",
                columns: new[] { "UserId", "WaterPointId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InfrastructureConfirmations_WaterPointId_EvidenceVersion_Cr~",
                table: "InfrastructureConfirmations",
                columns: new[] { "WaterPointId", "EvidenceVersion", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InfrastructureConfirmations");

            migrationBuilder.DropColumn(
                name: "EvidenceVersion",
                table: "WaterPoints");

            migrationBuilder.DropColumn(
                name: "EvidenceVersion",
                table: "TrashBins");
        }
    }
}
