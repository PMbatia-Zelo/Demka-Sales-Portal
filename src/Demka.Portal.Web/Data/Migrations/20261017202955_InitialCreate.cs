using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Demka.Portal.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Entity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutletOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CycleId = table.Column<int>(type: "int", nullable: false),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    BaselinePerMonth = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutletOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outlets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Chain = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RegionBasis = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    HqCreditAccount = table.Column<bool>(type: "bit", nullable: false),
                    InScope = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outlets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegionTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetVersionId = table.Column<int>(type: "int", nullable: false),
                    Region = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Target = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Expansion = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegionTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TargetCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BaselineStart = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetStart = table.Column<DateOnly>(type: "date", nullable: false),
                    TargetMonths = table.Column<int>(type: "int", nullable: false),
                    AssumptionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublishedVersion = table.Column<int>(type: "int", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetCycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RowsRead = table.Column<int>(type: "int", nullable: false),
                    RowsMatched = table.Column<int>(type: "int", nullable: false),
                    RowsRejected = table.Column<int>(type: "int", nullable: false),
                    UnmappedCustomers = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserId = table.Column<int>(type: "int", nullable: true),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TeamLeaderId = table.Column<int>(type: "int", nullable: true),
                    PinHash = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FailedPinAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MustChangePin = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Users_TeamLeaderId",
                        column: x => x.TeamLeaderId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Visits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    VisitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UploadBatchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerNo = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OutletId = table.Column<int>(type: "int", nullable: true),
                    Ignore = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerMappings_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SalesActuals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    IsProvisional = table.Column<bool>(type: "bit", nullable: false),
                    UploadBatchId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesActuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesActuals_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TargetVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CycleId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    AssumptionsJson = table.Column<string>(type: "nvarchar(max)", maxLength: 200, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedByUserId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TargetVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TargetVersions_TargetCycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "TargetCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CycleId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ExpectedUplift = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewerUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionPlans_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionPlans_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Allocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    HolderUserId = table.Column<int>(type: "int", nullable: true),
                    Route = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    VisitsInWindow = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Allocations_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Allocations_Users_HolderUserId",
                        column: x => x.HolderUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OutletTargets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetVersionId = table.Column<int>(type: "int", nullable: false),
                    OutletId = table.Column<int>(type: "int", nullable: false),
                    HolderUserId = table.Column<int>(type: "int", nullable: true),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    BaselinePerMonth = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Growth = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    Recovery = table.Column<bool>(type: "bit", nullable: false),
                    RecoveryUpliftPerMonth = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutletTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutletTargets_Outlets_OutletId",
                        column: x => x.OutletId,
                        principalTable: "Outlets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutletTargets_TargetVersions_TargetVersionId",
                        column: x => x.TargetVersionId,
                        principalTable: "TargetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionPlans_CycleId_Status",
                table: "ActionPlans",
                columns: new[] { "CycleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionPlans_OutletId",
                table: "ActionPlans",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionPlans_OwnerUserId",
                table: "ActionPlans",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_HolderUserId",
                table: "Allocations",
                column: "HolderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Allocations_OutletId_ValidTo",
                table: "Allocations",
                columns: new[] { "OutletId", "ValidTo" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMappings_CustomerNo",
                table: "CustomerMappings",
                column: "CustomerNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMappings_OutletId",
                table: "CustomerMappings",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_OutletOverrides_CycleId_OutletId",
                table: "OutletOverrides",
                columns: new[] { "CycleId", "OutletId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outlets_Region",
                table: "Outlets",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_OutletTargets_OutletId",
                table: "OutletTargets",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_OutletTargets_TargetVersionId_HolderUserId",
                table: "OutletTargets",
                columns: new[] { "TargetVersionId", "HolderUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutletTargets_TargetVersionId_OutletId_Month",
                table: "OutletTargets",
                columns: new[] { "TargetVersionId", "OutletId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegionTargets_TargetVersionId_Region_Month",
                table: "RegionTargets",
                columns: new[] { "TargetVersionId", "Region", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesActuals_OutletId_Month",
                table: "SalesActuals",
                columns: new[] { "OutletId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TargetCycles_Name",
                table: "TargetCycles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TargetVersions_CycleId_Version",
                table: "TargetVersions",
                columns: new[] { "CycleId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Phone",
                table: "Users",
                column: "Phone",
                unique: true,
                filter: "[Phone] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TeamLeaderId",
                table: "Users",
                column: "TeamLeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_OutletId_VisitedAt",
                table: "Visits",
                columns: new[] { "OutletId", "VisitedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionPlans");

            migrationBuilder.DropTable(
                name: "Allocations");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CustomerMappings");

            migrationBuilder.DropTable(
                name: "OutletOverrides");

            migrationBuilder.DropTable(
                name: "OutletTargets");

            migrationBuilder.DropTable(
                name: "RegionTargets");

            migrationBuilder.DropTable(
                name: "SalesActuals");

            migrationBuilder.DropTable(
                name: "UploadBatches");

            migrationBuilder.DropTable(
                name: "Visits");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "TargetVersions");

            migrationBuilder.DropTable(
                name: "Outlets");

            migrationBuilder.DropTable(
                name: "TargetCycles");
        }
    }
}
