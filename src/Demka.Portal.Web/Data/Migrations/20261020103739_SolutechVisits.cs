using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demka.Portal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SolutechVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "Visits",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckedOutAt",
                table: "Visits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ErpCode",
                table: "Visits",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ExternalId",
                table: "Visits",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "MinutesOnSite",
                table: "Visits",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Visits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepName",
                table: "Visits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ShopId",
                table: "Visits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShopName",
                table: "Visits",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ShopMappings",
                columns: table => new
                {
                    ShopId = table.Column<int>(type: "int", nullable: false),
                    ShopName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ErpCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OutletId = table.Column<int>(type: "int", nullable: true),
                    Ignore = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopMappings", x => x.ShopId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_ShopId",
                table: "Visits",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_VisitedAt",
                table: "Visits",
                column: "VisitedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopMappings");

            migrationBuilder.DropIndex(
                name: "IX_Visits_ShopId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_VisitedAt",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "CheckedOutAt",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ErpCode",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "MinutesOnSite",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "RepName",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ShopId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ShopName",
                table: "Visits");

            migrationBuilder.AlterColumn<int>(
                name: "OutletId",
                table: "Visits",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
