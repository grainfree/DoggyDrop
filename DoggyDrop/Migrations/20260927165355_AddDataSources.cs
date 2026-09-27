using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddDataSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DataSourceId",
                table: "TrashBins",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataSourceId",
                table: "Places",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DataDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrashBins_DataSourceId",
                table: "TrashBins",
                column: "DataSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Places_DataSourceId",
                table: "Places",
                column: "DataSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Places_DataSources_DataSourceId",
                table: "Places",
                column: "DataSourceId",
                principalTable: "DataSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TrashBins_DataSources_DataSourceId",
                table: "TrashBins",
                column: "DataSourceId",
                principalTable: "DataSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Places_DataSources_DataSourceId",
                table: "Places");

            migrationBuilder.DropForeignKey(
                name: "FK_TrashBins_DataSources_DataSourceId",
                table: "TrashBins");

            migrationBuilder.DropTable(
                name: "DataSources");

            migrationBuilder.DropIndex(
                name: "IX_TrashBins_DataSourceId",
                table: "TrashBins");

            migrationBuilder.DropIndex(
                name: "IX_Places_DataSourceId",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "DataSourceId",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "DataSourceId",
                table: "Places");
        }
    }
}
