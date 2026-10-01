using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MachineDeclarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LegalBasis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MachineName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Function = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    YearOfConstruction = table.Column<int>(type: "integer", nullable: true),
                    OtherLegislation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Standards = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NotifiedBody = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TechnicalFileKeeper = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Place = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SignatoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SignatoryRole = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineDeclarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineDeclarations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MachineTechnicalFileItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Optional = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineTechnicalFileItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineTechnicalFileItems_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MachineTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TestDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerWitness = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineTests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineTests_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MachineTestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MachineTestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Expected = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Measured = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MachineTestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MachineTestItems_MachineTests_MachineTestId",
                        column: x => x.MachineTestId,
                        principalTable: "MachineTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MachineDeclarations_Number",
                table: "MachineDeclarations",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineDeclarations_WorkOrderId",
                table: "MachineDeclarations",
                column: "WorkOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTechnicalFileItems_WorkOrderId_Code",
                table: "MachineTechnicalFileItems",
                columns: new[] { "WorkOrderId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTestItems_MachineTestId",
                table: "MachineTestItems",
                column: "MachineTestId");

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_Number",
                table: "MachineTests",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTests_WorkOrderId",
                table: "MachineTests",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MachineDeclarations");

            migrationBuilder.DropTable(
                name: "MachineTechnicalFileItems");

            migrationBuilder.DropTable(
                name: "MachineTestItems");

            migrationBuilder.DropTable(
                name: "MachineTests");
        }
    }
}
