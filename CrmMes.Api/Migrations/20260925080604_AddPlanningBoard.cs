using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrmMes.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanningBoard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanningCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ColorHex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanningProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    SequenceNumber = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningProjects_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlanningCells",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanningProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanningCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningCells_PlanningCategories_PlanningCategoryId",
                        column: x => x.PlanningCategoryId,
                        principalTable: "PlanningCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanningCells_PlanningProjects_PlanningProjectId",
                        column: x => x.PlanningProjectId,
                        principalTable: "PlanningProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningCategories_Code",
                table: "PlanningCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningCells_PlanningCategoryId",
                table: "PlanningCells",
                column: "PlanningCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningCells_PlanningProjectId_PlanningCategoryId_WeekStart",
                table: "PlanningCells",
                columns: new[] { "PlanningProjectId", "PlanningCategoryId", "WeekStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningProjects_WorkOrderId",
                table: "PlanningProjects",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanningCells");

            migrationBuilder.DropTable(
                name: "PlanningCategories");

            migrationBuilder.DropTable(
                name: "PlanningProjects");
        }
    }
}
