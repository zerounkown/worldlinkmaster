using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorSkuAndVendorColorCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorSku",
                table: "Products",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorColorCode",
                table: "ProductColors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_VendorSku",
                table: "Products",
                column: "VendorSku");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_VendorSku",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VendorSku",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VendorColorCode",
                table: "ProductColors");
        }
    }
}
