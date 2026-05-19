using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DoggyDrop.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialReactionTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReactionType",
                table: "WalkReactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "paw");

            migrationBuilder.AddColumn<string>(
                name: "ReactionType",
                table: "WalkPhotoReactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "heart");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReactionType",
                table: "WalkReactions");

            migrationBuilder.DropColumn(
                name: "ReactionType",
                table: "WalkPhotoReactions");
        }
    }
}
