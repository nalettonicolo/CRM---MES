using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFoodSafetyAndSiteReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NetQuantity",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesName",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShelfLifeDays",
                table: "Products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageConditions",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseByDate",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Allergens",
                table: "Materials",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngredientName",
                table: "Materials",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gs1CompanyPrefix",
                table: "CompanyProfiles",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastSsccSerial",
                table: "CompanyProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "HaccpControlPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Hazard = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MinValue = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    MaxValue = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    Frequency = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CorrectiveActionHint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaccpControlPoints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogisticUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sscc = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    TransportDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProductName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    BestBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogisticUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LogisticUnits_TransportDocuments_TransportDocumentId",
                        column: x => x.TransportDocumentId,
                        principalTable: "TransportDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LogisticUnits_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SiteReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    WorkDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SiteAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SignedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SignatureImage = table.Column<string>(type: "character varying(400000)", maxLength: 400000, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReports_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HaccpReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ControlPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    Compliant = table.Column<bool>(type: "boolean", nullable: false),
                    CorrectiveAction = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperatorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HaccpReadings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HaccpReadings_HaccpControlPoints_ControlPointId",
                        column: x => x.ControlPointId,
                        principalTable: "HaccpControlPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SiteReportHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    TechnicianName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    Minutes = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReportHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReportHours_SiteReports_SiteReportId",
                        column: x => x.SiteReportId,
                        principalTable: "SiteReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SiteReportHours_WorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "WorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SiteReportMaterials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SiteReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteReportMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SiteReportMaterials_SiteReports_SiteReportId",
                        column: x => x.SiteReportId,
                        principalTable: "SiteReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HaccpReadings_ControlPointId_ReadAt",
                table: "HaccpReadings",
                columns: new[] { "ControlPointId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LogisticUnits_Sscc",
                table: "LogisticUnits",
                column: "Sscc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogisticUnits_TransportDocumentId",
                table: "LogisticUnits",
                column: "TransportDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_LogisticUnits_WorkOrderId",
                table: "LogisticUnits",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportHours_SiteReportId",
                table: "SiteReportHours",
                column: "SiteReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportHours_WorkCenterId",
                table: "SiteReportHours",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReportMaterials_SiteReportId",
                table: "SiteReportMaterials",
                column: "SiteReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SiteReports_Code",
                table: "SiteReports",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SiteReports_WorkOrderId",
                table: "SiteReports",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HaccpReadings");

            migrationBuilder.DropTable(
                name: "LogisticUnits");

            migrationBuilder.DropTable(
                name: "SiteReportHours");

            migrationBuilder.DropTable(
                name: "SiteReportMaterials");

            migrationBuilder.DropTable(
                name: "HaccpControlPoints");

            migrationBuilder.DropTable(
                name: "SiteReports");

            migrationBuilder.DropColumn(
                name: "NetQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalesName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ShelfLifeDays",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StorageConditions",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UseByDate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Allergens",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "IngredientName",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "Gs1CompanyPrefix",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "LastSsccSerial",
                table: "CompanyProfiles");
        }
    }
}
