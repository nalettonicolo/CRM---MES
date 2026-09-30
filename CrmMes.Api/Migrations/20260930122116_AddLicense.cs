using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LicenseCheckedAt",
                table: "CompanyProfiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensePublicKey",
                table: "CompanyProfiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseToken",
                table: "CompanyProfiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseCheckedAt",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "LicensePublicKey",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "LicenseToken",
                table: "CompanyProfiles");
        }
    }
}
