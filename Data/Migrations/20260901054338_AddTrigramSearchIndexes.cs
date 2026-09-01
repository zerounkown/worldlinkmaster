using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrigramSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Barcode_Trgm",
                table: "ProductVariants",
                column: "Barcode")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Description_Trgm",
                table: "Products",
                column: "Description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name_Trgm",
                table: "Products",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_NameAr_Trgm",
                table: "Products",
                column: "NameAr")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShortDescription_Trgm",
                table: "Products",
                column: "ShortDescription")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShortDescriptionAr_Trgm",
                table: "Products",
                column: "ShortDescriptionAr")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products",
                column: "Sku")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Colors_Name_Trgm",
                table: "Colors",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Name_Trgm",
                table: "Brands",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_Barcode_Trgm",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_Description_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_NameAr_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ShortDescription_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ShortDescriptionAr_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Colors_Name_Trgm",
                table: "Colors");

            migrationBuilder.DropIndex(
                name: "IX_Brands_Name_Trgm",
                table: "Brands");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
