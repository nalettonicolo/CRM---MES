using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialLotTraceability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductLotNumber",
                table: "WorkOrders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MaterialLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    InitialQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLots_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaterialLots_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MaterialLotConsumptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaterialLotId = table.Column<Guid>(type: "uuid", nullable: false),
                    WithdrawalItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLotConsumptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLotConsumptions_MaterialLots_MaterialLotId",
                        column: x => x.MaterialLotId,
                        principalTable: "MaterialLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialLotConsumptions_WithdrawalItems_WithdrawalItemId",
                        column: x => x.WithdrawalItemId,
                        principalTable: "WithdrawalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLotConsumptions_MaterialLotId",
                table: "MaterialLotConsumptions",
                column: "MaterialLotId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLotConsumptions_WithdrawalItemId",
                table: "MaterialLotConsumptions",
                column: "WithdrawalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_MaterialCode_LotNumber",
                table: "MaterialLots",
                columns: new[] { "MaterialCode", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_PurchaseOrderId",
                table: "MaterialLots",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_SupplierId",
                table: "MaterialLots",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaterialLotConsumptions");

            migrationBuilder.DropTable(
                name: "MaterialLots");

            migrationBuilder.DropColumn(
                name: "ProductLotNumber",
                table: "WorkOrders");
        }
    }
}
