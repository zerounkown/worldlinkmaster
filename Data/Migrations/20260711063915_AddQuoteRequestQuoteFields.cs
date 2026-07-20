using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorldLinkMaster.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteRequestQuoteFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminResponse",
                table: "QuoteRequests");

            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                table: "QuoteRequests",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedPrice",
                table: "QuoteRequests",
                type: "decimal(10,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondedAt",
                table: "QuoteRequests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminNotes",
                table: "QuoteRequests");

            migrationBuilder.DropColumn(
                name: "QuotedPrice",
                table: "QuoteRequests");

            migrationBuilder.DropColumn(
                name: "RespondedAt",
                table: "QuoteRequests");

            migrationBuilder.AddColumn<string>(
                name: "AdminResponse",
                table: "QuoteRequests",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
