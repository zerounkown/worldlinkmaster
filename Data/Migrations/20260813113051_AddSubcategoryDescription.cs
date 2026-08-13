using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcategoryDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Subcategories",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "Subcategories",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Subcategories");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "Subcategories");
        }
    }
}
