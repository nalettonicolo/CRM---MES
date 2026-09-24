using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitTraceabilityAndPurchaseOrderDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedDeliveryDate",
                table: "PurchaseOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkOrderUnitMaterialLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderUnitMaterialLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderUnitMaterialLots_MaterialLots_MaterialLotId",
                        column: x => x.MaterialLotId,
                        principalTable: "MaterialLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrderUnitMaterialLots_WorkOrderUnits_WorkOrderUnitId",
                        column: x => x.WorkOrderUnitId,
                        principalTable: "WorkOrderUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderUnitOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderUnitOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderUnitOperations_WorkOrderOperations_WorkOrderOperat~",
                        column: x => x.WorkOrderOperationId,
                        principalTable: "WorkOrderOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkOrderUnitOperations_WorkOrderUnits_WorkOrderUnitId",
                        column: x => x.WorkOrderUnitId,
                        principalTable: "WorkOrderUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnitMaterialLots_MaterialLotId",
                table: "WorkOrderUnitMaterialLots",
                column: "MaterialLotId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnitMaterialLots_WorkOrderUnitId",
                table: "WorkOrderUnitMaterialLots",
                column: "WorkOrderUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnitOperations_WorkOrderOperationId",
                table: "WorkOrderUnitOperations",
                column: "WorkOrderOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnitOperations_WorkOrderUnitId_WorkOrderOperationId",
                table: "WorkOrderUnitOperations",
                columns: new[] { "WorkOrderUnitId", "WorkOrderOperationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkOrderUnitMaterialLots");

            migrationBuilder.DropTable(
                name: "WorkOrderUnitOperations");

            migrationBuilder.DropColumn(
                name: "ExpectedDeliveryDate",
                table: "PurchaseOrders");
        }
    }
}
