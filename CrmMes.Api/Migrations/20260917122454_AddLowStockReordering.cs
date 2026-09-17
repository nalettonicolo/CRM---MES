using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLowStockReordering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MissingMaterials_WithdrawalSlips_WithdrawalSlipId",
                table: "MissingMaterials");

            migrationBuilder.AlterColumn<Guid>(
                name: "WithdrawalSlipId",
                table: "MissingMaterials",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_MissingMaterials_WithdrawalSlips_WithdrawalSlipId",
                table: "MissingMaterials",
                column: "WithdrawalSlipId",
                principalTable: "WithdrawalSlips",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MissingMaterials_WithdrawalSlips_WithdrawalSlipId",
                table: "MissingMaterials");

            migrationBuilder.AlterColumn<Guid>(
                name: "WithdrawalSlipId",
                table: "MissingMaterials",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MissingMaterials_WithdrawalSlips_WithdrawalSlipId",
                table: "MissingMaterials",
                column: "WithdrawalSlipId",
                principalTable: "WithdrawalSlips",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
