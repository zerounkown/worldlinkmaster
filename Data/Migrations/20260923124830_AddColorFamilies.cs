using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddColorFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FamilyId",
                table: "Colors",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ColorFamilies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    HexCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    SwatchImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColorFamilies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Colors_FamilyId",
                table: "Colors",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_ColorFamilies_Code",
                table: "ColorFamilies",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Colors_ColorFamilies_FamilyId",
                table: "Colors",
                column: "FamilyId",
                principalTable: "ColorFamilies",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Colors_ColorFamilies_FamilyId",
                table: "Colors");

            migrationBuilder.DropTable(
                name: "ColorFamilies");

            migrationBuilder.DropIndex(
                name: "IX_Colors_FamilyId",
                table: "Colors");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "Colors");
        }
    }
}
