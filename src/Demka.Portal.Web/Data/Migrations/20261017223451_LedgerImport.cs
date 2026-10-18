using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demka.Portal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class LedgerImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerMappings_CustomerNo",
                table: "CustomerMappings");

            migrationBuilder.AddColumn<DateOnly>(
                name: "RefreshFrom",
                table: "UploadBatches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryJson",
                table: "UploadBatches",
                type: "nvarchar(max)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "CustomerMappings",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "CustomerMappings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "CustomerMappings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "CustomerMappings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MonthStatuses",
                columns: table => new
                {
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthStatuses", x => x.Month);
                });

            migrationBuilder.CreateTable(
                name: "UnmappedLedgerNames",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UploadBatchId = table.Column<int>(type: "int", nullable: false),
                    CustomerNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PostingGroup = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Lines = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnmappedLedgerNames", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMappings_CustomerNo_NameKey",
                table: "CustomerMappings",
                columns: new[] { "CustomerNo", "NameKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnmappedLedgerNames_UploadBatchId_CustomerNo_NameKey",
                table: "UnmappedLedgerNames",
                columns: new[] { "UploadBatchId", "CustomerNo", "NameKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthStatuses");

            migrationBuilder.DropTable(
                name: "UnmappedLedgerNames");

            migrationBuilder.DropIndex(
                name: "IX_CustomerMappings_CustomerNo_NameKey",
                table: "CustomerMappings");

            migrationBuilder.DropColumn(
                name: "RefreshFrom",
                table: "UploadBatches");

            migrationBuilder.DropColumn(
                name: "SummaryJson",
                table: "UploadBatches");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CustomerMappings");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CustomerMappings");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "CustomerMappings");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "CustomerMappings");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMappings_CustomerNo",
                table: "CustomerMappings",
                column: "CustomerNo",
                unique: true);
        }
    }
}
