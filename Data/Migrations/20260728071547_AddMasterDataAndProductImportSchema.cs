using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDataAndProductImportSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Subcategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Subcategories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Subcategories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Sizes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Sizes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelAr",
                table: "Sizes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NumericValue",
                table: "Sizes",
                type: "numeric(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SizeGroupId",
                table: "Sizes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "Sizes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "ProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AlertQty",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowBackorder",
                table: "ProductVariants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "ProductVariants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductColorId",
                table: "ProductVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "ProductVariants",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPublished",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitleAr",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoTitleEn",
                table: "Products",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SizeGroupId",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Products",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Colors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Colors",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Colors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Brands",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Brands",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Brands",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttributeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Filterable = table.Column<bool>(type: "boolean", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttributeDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductColors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ColorId = table.Column<int>(type: "integer", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    DefaultColor = table.Column<bool>(type: "boolean", nullable: false),
                    SwatchImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductColors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductColors_Colors_ColorId",
                        column: x => x.ColorId,
                        principalTable: "Colors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductColors_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SizeGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    NameEn = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    UnitType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SizeGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    AttributeDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ValueEn = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ValueAr = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    UseInFilter = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_AttributeDefinitions_AttributeDefini~",
                        column: x => x.AttributeDefinitionId,
                        principalTable: "AttributeDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductColorId = table.Column<int>(type: "integer", nullable: true),
                    MediaScope = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MediaRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    MediaUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsColorMain = table.Column<bool>(type: "boolean", nullable: false),
                    ShowInGallery = table.Column<bool>(type: "boolean", nullable: false),
                    AltTextEn = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    AltTextAr = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductMedia_ProductColors_ProductColorId",
                        column: x => x.ProductColorId,
                        principalTable: "ProductColors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductMedia_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subcategories_Code",
                table: "Subcategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sizes_Code",
                table: "Sizes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sizes_SizeGroupId",
                table: "Sizes",
                column: "SizeGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductColorId",
                table: "ProductVariants",
                column: "ProductColorId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SizeGroupId",
                table: "Products",
                column: "SizeGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Colors_Code",
                table: "Colors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Code",
                table: "Categories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Brands_Code",
                table: "Brands",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttributeDefinitions_Code",
                table: "AttributeDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_AttributeDefinitionId",
                table: "ProductAttributeValues",
                column: "AttributeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_ProductId",
                table: "ProductAttributeValues",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductColors_Code",
                table: "ProductColors",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductColors_ColorId",
                table: "ProductColors",
                column: "ColorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductColors_ProductId_ColorId",
                table: "ProductColors",
                columns: new[] { "ProductId", "ColorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductColorId",
                table: "ProductMedia",
                column: "ProductColorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductMedia_ProductId",
                table: "ProductMedia",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SizeGroups_Code",
                table: "SizeGroups",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_SizeGroups_SizeGroupId",
                table: "Products",
                column: "SizeGroupId",
                principalTable: "SizeGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductVariants_ProductColors_ProductColorId",
                table: "ProductVariants",
                column: "ProductColorId",
                principalTable: "ProductColors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sizes_SizeGroups_SizeGroupId",
                table: "Sizes",
                column: "SizeGroupId",
                principalTable: "SizeGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_SizeGroups_SizeGroupId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductVariants_ProductColors_ProductColorId",
                table: "ProductVariants");

            migrationBuilder.DropForeignKey(
                name: "FK_Sizes_SizeGroups_SizeGroupId",
                table: "Sizes");

            migrationBuilder.DropTable(
                name: "ProductAttributeValues");

            migrationBuilder.DropTable(
                name: "ProductMedia");

            migrationBuilder.DropTable(
                name: "SizeGroups");

            migrationBuilder.DropTable(
                name: "AttributeDefinitions");

            migrationBuilder.DropTable(
                name: "ProductColors");

            migrationBuilder.DropIndex(
                name: "IX_Subcategories_Code",
                table: "Subcategories");

            migrationBuilder.DropIndex(
                name: "IX_Sizes_Code",
                table: "Sizes");

            migrationBuilder.DropIndex(
                name: "IX_Sizes_SizeGroupId",
                table: "Sizes");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_Barcode",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_ProductVariants_ProductColorId",
                table: "ProductVariants");

            migrationBuilder.DropIndex(
                name: "IX_Products_SizeGroupId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Colors_Code",
                table: "Colors");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Code",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Brands_Code",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Subcategories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Subcategories");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Subcategories");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "LabelAr",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "NumericValue",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "SizeGroupId",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "Sizes");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "AlertQty",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "AllowBackorder",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "ProductColorId",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "ProductVariants");

            migrationBuilder.DropColumn(
                name: "IsPublished",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SeoTitleAr",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SeoTitleEn",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SizeGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Colors");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Colors");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Colors");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Brands");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "Brands");
        }
    }
}
