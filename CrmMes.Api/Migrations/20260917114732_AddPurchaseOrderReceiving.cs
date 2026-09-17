using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "PurchaseOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "PurchaseOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MissingMaterialId",
                table: "PurchaseOrderItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "PurchaseOrderItems",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderItems_MissingMaterialId",
                table: "PurchaseOrderItems",
                column: "MissingMaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderItems_MissingMaterials_MissingMaterialId",
                table: "PurchaseOrderItems",
                column: "MissingMaterialId",
                principalTable: "MissingMaterials",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderItems_MissingMaterials_MissingMaterialId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderItems_MissingMaterialId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "MissingMaterialId",
                table: "PurchaseOrderItems");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "PurchaseOrderItems");
        }
    }
}
