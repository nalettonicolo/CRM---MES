using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPanelVerificationAndLotExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "MaterialLots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PanelVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Standard = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginalManufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SystemReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RatedVoltage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    RatedCurrent = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    RatedFrequency = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    ShortTimeWithstandCurrent = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ConditionalShortCircuitCurrent = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    IpRating = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InternalSeparation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EarthingSystem = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    InsulationResistanceMOhm = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    DielectricTestVoltage = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelVerifications_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelVerificationChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PanelVerificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Clause = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelVerificationChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelVerificationChecks_PanelVerifications_PanelVerificatio~",
                        column: x => x.PanelVerificationId,
                        principalTable: "PanelVerifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PanelVerificationChecks_PanelVerificationId",
                table: "PanelVerificationChecks",
                column: "PanelVerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelVerifications_WorkOrderId",
                table: "PanelVerifications",
                column: "WorkOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PanelVerificationChecks");

            migrationBuilder.DropTable(
                name: "PanelVerifications");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "MaterialLots");
        }
    }
}
