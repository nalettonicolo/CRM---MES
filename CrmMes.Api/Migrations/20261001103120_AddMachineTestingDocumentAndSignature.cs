using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMachineTestingDocumentAndSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TechnicalDocumentId",
                table: "MachineTechnicalFileItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureImage",
                table: "MachineDeclarations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MachineTechnicalFileItems_TechnicalDocumentId",
                table: "MachineTechnicalFileItems",
                column: "TechnicalDocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_MachineTechnicalFileItems_TechnicalDocuments_TechnicalDocum~",
                table: "MachineTechnicalFileItems",
                column: "TechnicalDocumentId",
                principalTable: "TechnicalDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MachineTechnicalFileItems_TechnicalDocuments_TechnicalDocum~",
                table: "MachineTechnicalFileItems");

            migrationBuilder.DropIndex(
                name: "IX_MachineTechnicalFileItems_TechnicalDocumentId",
                table: "MachineTechnicalFileItems");

            migrationBuilder.DropColumn(
                name: "TechnicalDocumentId",
                table: "MachineTechnicalFileItems");

            migrationBuilder.DropColumn(
                name: "SignatureImage",
                table: "MachineDeclarations");
        }
    }
}
