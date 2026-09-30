using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "RefreshTokens",
                type: "text",
                nullable: false,
                // Sessions opened before channels existed come from the desktop program (or the phone
                // page, which logged in the same way): an empty value would end them at the next renewal.
                defaultValue: "desktop");

            migrationBuilder.AddColumn<string>(
                name: "AccessChannels",
                table: "CompanyProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "AccessChannels",
                table: "CompanyProfiles");
        }
    }
}
