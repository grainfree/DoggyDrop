using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddTrashBinsToApplicationUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicationUserId",
                table: "TrashBins",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrashBins_ApplicationUserId",
                table: "TrashBins",
                column: "ApplicationUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TrashBins_AspNetUsers_ApplicationUserId",
                table: "TrashBins",
                column: "ApplicationUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TrashBins_AspNetUsers_ApplicationUserId",
                table: "TrashBins");

            migrationBuilder.DropIndex(
                name: "IX_TrashBins_ApplicationUserId",
                table: "TrashBins");

            migrationBuilder.DropColumn(
                name: "ApplicationUserId",
                table: "TrashBins");
        }
    }
}
