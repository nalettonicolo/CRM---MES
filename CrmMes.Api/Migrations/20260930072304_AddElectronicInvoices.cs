using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddElectronicInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "CompanyProfiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "CompanyProfiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FiscalCode",
                table: "CompanyProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "CompanyProfiles",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "CompanyProfiles",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Province",
                table: "CompanyProfiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReaNumber",
                table: "CompanyProfiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReaOffice",
                table: "CompanyProfiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "CompanyProfiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegime",
                table: "CompanyProfiles",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CustomerFiscalData",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SdiCode = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Pec = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Street = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    City = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Province = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerFiscalData", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_CustomerFiscalData_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    PaymentDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    VatNature = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    TransportDocumentId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceTransportDocuments",
                columns: table => new
                {
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransportDocumentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTransportDocuments", x => new { x.InvoiceId, x.TransportDocumentId });
                    table.ForeignKey(
                        name: "FK_InvoiceTransportDocuments_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceTransportDocuments_TransportDocuments_TransportDocum~",
                        column: x => x.TransportDocumentId,
                        principalTable: "TransportDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Year_Number",
                table: "Invoices",
                columns: new[] { "Year", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTransportDocuments_TransportDocumentId",
                table: "InvoiceTransportDocuments",
                column: "TransportDocumentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerFiscalData");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoiceTransportDocuments");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropColumn(
                name: "City",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "FiscalCode",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "Province",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ReaNumber",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ReaOffice",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "TaxRegime",
                table: "CompanyProfiles");
        }
    }
}
