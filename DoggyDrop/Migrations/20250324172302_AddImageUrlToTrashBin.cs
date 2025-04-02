using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToTrashBin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "TrashBins",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "TrashBins");
        }
    }
}
