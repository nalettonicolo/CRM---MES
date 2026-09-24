using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkOperatorActionsToUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "WorkOrderOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StartedByUserId",
                table: "WorkOrderOperations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                table: "OperationDowntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportedByUserId",
                table: "OperationDowntimes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReportedByUserId",
                table: "NonConformities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderOperations_CompletedByUserId",
                table: "WorkOrderOperations",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderOperations_StartedByUserId",
                table: "WorkOrderOperations",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationDowntimes_ClosedByUserId",
                table: "OperationDowntimes",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperationDowntimes_ReportedByUserId",
                table: "OperationDowntimes",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NonConformities_ReportedByUserId",
                table: "NonConformities",
                column: "ReportedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_NonConformities_Users_ReportedByUserId",
                table: "NonConformities",
                column: "ReportedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OperationDowntimes_Users_ClosedByUserId",
                table: "OperationDowntimes",
                column: "ClosedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_OperationDowntimes_Users_ReportedByUserId",
                table: "OperationDowntimes",
                column: "ReportedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderOperations_Users_CompletedByUserId",
                table: "WorkOrderOperations",
                column: "CompletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrderOperations_Users_StartedByUserId",
                table: "WorkOrderOperations",
                column: "StartedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NonConformities_Users_ReportedByUserId",
                table: "NonConformities");

            migrationBuilder.DropForeignKey(
                name: "FK_OperationDowntimes_Users_ClosedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_OperationDowntimes_Users_ReportedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderOperations_Users_CompletedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrderOperations_Users_StartedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderOperations_CompletedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrderOperations_StartedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropIndex(
                name: "IX_OperationDowntimes_ClosedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropIndex(
                name: "IX_OperationDowntimes_ReportedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropIndex(
                name: "IX_NonConformities_ReportedByUserId",
                table: "NonConformities");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "StartedByUserId",
                table: "WorkOrderOperations");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropColumn(
                name: "ReportedByUserId",
                table: "OperationDowntimes");

            migrationBuilder.DropColumn(
                name: "ReportedByUserId",
                table: "NonConformities");
        }
    }
}
