using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTransportDocumentsAndSubcontracting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TransportDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReasonDetail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    RecipientAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecipientVatNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TransportBy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CarrierId = table.Column<Guid>(type: "uuid", nullable: true),
                    Port = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    GoodsAppearance = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Packages = table.Column<int>(type: "integer", nullable: true),
                    GrossWeightKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    TransportStartAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedReturnAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportDocuments_Carriers_CarrierId",
                        column: x => x.CarrierId,
                        principalTable: "Carriers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportDocuments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportDocuments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportDocuments_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TransportDocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LotNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransportDocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransportDocumentLines_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportDocumentLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransportDocumentLines_TransportDocuments_TransportDocument~",
                        column: x => x.TransportDocumentId,
                        principalTable: "TransportDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubcontractingReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportDocumentLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ScrapQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    ReturnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupplierDocumentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubcontractingReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubcontractingReturns_TransportDocumentLines_TransportDocum~",
                        column: x => x.TransportDocumentLineId,
                        principalTable: "TransportDocumentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubcontractingReturns_TransportDocumentLineId",
                table: "SubcontractingReturns",
                column: "TransportDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocumentLines_MaterialId",
                table: "TransportDocumentLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocumentLines_ProductId",
                table: "TransportDocumentLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocumentLines_TransportDocumentId",
                table: "TransportDocumentLines",
                column: "TransportDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_CarrierId",
                table: "TransportDocuments",
                column: "CarrierId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_CustomerId",
                table: "TransportDocuments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_Status_Reason",
                table: "TransportDocuments",
                columns: new[] { "Status", "Reason" });

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_SupplierId",
                table: "TransportDocuments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_WorkOrderId",
                table: "TransportDocuments",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TransportDocuments_Year_Number",
                table: "TransportDocuments",
                columns: new[] { "Year", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubcontractingReturns");

            migrationBuilder.DropTable(
                name: "TransportDocumentLines");

            migrationBuilder.DropTable(
                name: "TransportDocuments");
        }
    }
}
