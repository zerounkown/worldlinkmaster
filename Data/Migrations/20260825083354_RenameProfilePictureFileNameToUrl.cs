using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameProfilePictureFileNameToUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProfilePictureFileName",
                table: "AspNetUsers",
                newName: "ProfilePictureUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProfilePictureUrl",
                table: "AspNetUsers",
                newName: "ProfilePictureFileName");
        }
    }
}
