using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkOrderUnitId",
                table: "NonConformities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkOrderUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderUnits_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NonConformities_WorkOrderUnitId",
                table: "NonConformities",
                column: "WorkOrderUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnits_SerialNumber",
                table: "WorkOrderUnits",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderUnits_WorkOrderId",
                table: "WorkOrderUnits",
                column: "WorkOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_NonConformities_WorkOrderUnits_WorkOrderUnitId",
                table: "NonConformities",
                column: "WorkOrderUnitId",
                principalTable: "WorkOrderUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NonConformities_WorkOrderUnits_WorkOrderUnitId",
                table: "NonConformities");

            migrationBuilder.DropTable(
                name: "WorkOrderUnits");

            migrationBuilder.DropIndex(
                name: "IX_NonConformities_WorkOrderUnitId",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "WorkOrderUnitId",
                table: "NonConformities");
        }
    }
}
