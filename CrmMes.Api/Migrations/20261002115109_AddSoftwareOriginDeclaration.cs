using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftwareOriginDeclaration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SoftwareDevelopmentPlaces",
                table: "CompanyProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "SoftwareEuDevelopmentPercent",
                table: "CompanyProfiles",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "SoftwareOriginSignatory",
                table: "CompanyProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SoftwareOriginUpdatedAt",
                table: "CompanyProfiles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SoftwareDevelopmentPlaces",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "SoftwareEuDevelopmentPercent",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "SoftwareOriginSignatory",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "SoftwareOriginUpdatedAt",
                table: "CompanyProfiles");
        }
    }
}
