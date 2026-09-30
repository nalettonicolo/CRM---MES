using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AreaId",
                table: "WorkCenters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Activities",
                table: "CompanyProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentType",
                table: "Areas",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenters_AreaId",
                table: "WorkCenters",
                column: "AreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkCenters_Areas_AreaId",
                table: "WorkCenters",
                column: "AreaId",
                principalTable: "Areas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkCenters_Areas_AreaId",
                table: "WorkCenters");

            migrationBuilder.DropIndex(
                name: "IX_WorkCenters_AreaId",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "AreaId",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "Activities",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "DepartmentType",
                table: "Areas");
        }
    }
}
