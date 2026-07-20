using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MerchantId",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MerchantId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Merchants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BusinessName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(170)", maxLength: 170, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StripeAccountId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    StripeOnboardingComplete = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Merchants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MerchantPayouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    PlatformFee = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    StripeTransferId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantPayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantPayouts_Merchants_MerchantId",
                        column: x => x.MerchantId,
                        principalTable: "Merchants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MerchantPayouts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_MerchantId",
                table: "Products",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_MerchantId",
                table: "OrderItems",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantPayouts_MerchantId",
                table: "MerchantPayouts",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantPayouts_OrderId",
                table: "MerchantPayouts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_Slug",
                table: "Merchants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_UserId",
                table: "Merchants",
                column: "UserId");

            // Backfill: give pre-existing catalog data (seeded before the marketplace feature
            // existed) a default "World Link Master" merchant so the FK constraints below can
            // be applied. No-op on a fresh database with no admin user / no products yet -
            // SeedData creates the default merchant and assigns it to newly-seeded products instead.
            migrationBuilder.Sql(@"
                INSERT INTO Merchants (UserId, BusinessName, Slug, Description, StripeOnboardingComplete, CreatedAt)
                SELECT TOP 1 Id, 'World Link Master', 'world-link-master', 'World Link Master''s own first-party catalog.', 0, GETUTCDATE()
                FROM AspNetUsers
                WHERE Email = 'admin@worldlinkmaster.com'
                AND NOT EXISTS (SELECT 1 FROM Merchants WHERE Slug = 'world-link-master');

                UPDATE Products SET MerchantId = (SELECT Id FROM Merchants WHERE Slug = 'world-link-master')
                WHERE MerchantId = 0 AND EXISTS (SELECT 1 FROM Merchants WHERE Slug = 'world-link-master');

                UPDATE OrderItems SET MerchantId = (SELECT Id FROM Merchants WHERE Slug = 'world-link-master')
                WHERE MerchantId = 0 AND EXISTS (SELECT 1 FROM Merchants WHERE Slug = 'world-link-master');
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Merchants_MerchantId",
                table: "OrderItems",
                column: "MerchantId",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Merchants_MerchantId",
                table: "Products",
                column: "MerchantId",
                principalTable: "Merchants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Merchants_MerchantId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Merchants_MerchantId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "MerchantPayouts");

            migrationBuilder.DropTable(
                name: "Merchants");

            migrationBuilder.DropIndex(
                name: "IX_Products_MerchantId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_MerchantId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MerchantId",
                table: "OrderItems");
        }
    }
}
