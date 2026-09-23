using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompletedBy",
                table: "WorkOrderOperations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartedBy",
                table: "WorkOrderOperations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "OperationDowntimes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedBy",
                table: "OperationDowntimes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportedBy",
                table: "NonConformities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "StartedBy",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "OperationDowntimes");

            migrationBuilder.DropColumn(
                name: "ReportedBy",
                table: "OperationDowntimes");

            migrationBuilder.DropColumn(
                name: "ReportedBy",
                table: "NonConformities");
        }
    }
}
