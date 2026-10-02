using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPayables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierVat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PaymentScheduleEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RemindedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderCount = table.Column<int>(type: "integer", nullable: false),
                    ReminderNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentScheduleEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentScheduleEntries_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentScheduleEntries_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    VatNature = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLines_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentScheduleEntries_Direction_Status_DueDate",
                table: "PaymentScheduleEntries",
                columns: new[] { "Direction", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentScheduleEntries_InvoiceId",
                table: "PaymentScheduleEntries",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentScheduleEntries_PurchaseInvoiceId",
                table: "PaymentScheduleEntries",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLines_PurchaseInvoiceId",
                table: "PurchaseInvoiceLines",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_ContentHash",
                table: "PurchaseInvoices",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId",
                table: "PurchaseInvoices",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierVat_DocumentNumber_DocumentDate",
                table: "PurchaseInvoices",
                columns: new[] { "SupplierVat", "DocumentNumber", "DocumentDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentScheduleEntries");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLines");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");
        }
    }
}
