using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "WorkCenters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SiteId",
                table: "Areas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCenters_SiteId",
                table: "WorkCenters",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_SiteId",
                table: "Areas",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Code",
                table: "Sites",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Areas_Sites_SiteId",
                table: "Areas",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkCenters_Sites_SiteId",
                table: "WorkCenters",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Areas_Sites_SiteId",
                table: "Areas");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkCenters_Sites_SiteId",
                table: "WorkCenters");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_WorkCenters_SiteId",
                table: "WorkCenters");

            migrationBuilder.DropIndex(
                name: "IX_Areas_SiteId",
                table: "Areas");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Areas");
        }
    }
}
