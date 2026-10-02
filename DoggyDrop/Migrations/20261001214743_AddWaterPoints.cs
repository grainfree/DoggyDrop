using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaterPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    IsRetired = table.Column<bool>(type: "boolean", nullable: false),
                    DataSourceId = table.Column<int>(type: "integer", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Potability = table.Column<int>(type: "integer", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false),
                    Seasonality = table.Column<int>(type: "integer", nullable: false),
                    DogAccess = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaterPoints", x => x.Id);
                    table.CheckConstraint("CK_WaterPoint_Approval", "NOT \"IsApproved\" OR (\"Potability\" = 1 AND \"Access\" <> 3)");
                    table.CheckConstraint("CK_WaterPoint_Coordinates", "\"Latitude\" >= -90 AND \"Latitude\" <= 90 AND \"Longitude\" >= -180 AND \"Longitude\" <= 180");
                    table.CheckConstraint("CK_WaterPoint_Evidence", "\"Potability\" BETWEEN 0 AND 2 AND \"Access\" BETWEEN 0 AND 3 AND \"Seasonality\" BETWEEN 0 AND 2 AND \"DogAccess\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_WaterPoints_DataSources_DataSourceId",
                        column: x => x.DataSourceId,
                        principalTable: "DataSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaterPoints_DataSourceId",
                table: "WaterPoints",
                column: "DataSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_WaterPoints_IsApproved_IsRetired",
                table: "WaterPoints",
                columns: new[] { "IsApproved", "IsRetired" });

            migrationBuilder.CreateIndex(
                name: "IX_WaterPoints_Latitude_Longitude",
                table: "WaterPoints",
                columns: new[] { "Latitude", "Longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaterPoints");
        }
    }
}
