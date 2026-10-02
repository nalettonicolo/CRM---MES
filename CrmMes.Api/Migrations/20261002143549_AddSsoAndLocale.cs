using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoAndLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalProvider",
                table: "Users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "CompanyProfiles",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "EUR");

            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "CompanyProfiles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "it-IT");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExternalProvider_ExternalSubject",
                table: "Users",
                columns: new[] { "ExternalProvider", "ExternalSubject" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_ExternalProvider_ExternalSubject",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "Locale",
                table: "CompanyProfiles");
        }
    }
}
