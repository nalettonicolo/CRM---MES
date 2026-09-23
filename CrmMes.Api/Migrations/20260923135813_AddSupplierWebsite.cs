using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierWebsite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "Suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Website",
                table: "Suppliers");
        }
    }
}
