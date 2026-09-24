using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCmmsAndQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Equipment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    WorkCenterId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipment_WorkCenters_WorkCenterId",
                        column: x => x.WorkCenterId,
                        principalTable: "WorkCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "QualityCheckpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NominalValue = table.Column<decimal>(type: "numeric", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    UpperLimit = table.Column<decimal>(type: "numeric", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityCheckpoints_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecurrenceDays = table.Column<int>(type: "integer", nullable: true),
                    OperationDowntimeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_Equipment_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_OperationDowntimes_OperationDowntimeId",
                        column: x => x.OperationDowntimeId,
                        principalTable: "OperationDowntimes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceTasks_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "QualityMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    MeasuredValue = table.Column<decimal>(type: "numeric", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MeasuredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QualityMeasurements_QualityCheckpoints_CheckpointId",
                        column: x => x.CheckpointId,
                        principalTable: "QualityCheckpoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QualityMeasurements_Users_MeasuredByUserId",
                        column: x => x.MeasuredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QualityMeasurements_WorkOrderUnits_WorkOrderUnitId",
                        column: x => x.WorkOrderUnitId,
                        principalTable: "WorkOrderUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QualityMeasurements_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_Code",
                table: "Equipment",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Equipment_WorkCenterId",
                table: "Equipment",
                column: "WorkCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_CompletedByUserId",
                table: "MaintenanceTasks",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_EquipmentId",
                table: "MaintenanceTasks",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTasks_OperationDowntimeId",
                table: "MaintenanceTasks",
                column: "OperationDowntimeId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityCheckpoints_ProductId",
                table: "QualityCheckpoints",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityMeasurements_CheckpointId",
                table: "QualityMeasurements",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityMeasurements_MeasuredByUserId",
                table: "QualityMeasurements",
                column: "MeasuredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityMeasurements_WorkOrderId",
                table: "QualityMeasurements",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityMeasurements_WorkOrderUnitId",
                table: "QualityMeasurements",
                column: "WorkOrderUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceTasks");

            migrationBuilder.DropTable(
                name: "QualityMeasurements");

            migrationBuilder.DropTable(
                name: "Equipment");

            migrationBuilder.DropTable(
                name: "QualityCheckpoints");
        }
    }
}
