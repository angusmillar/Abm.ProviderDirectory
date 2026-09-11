using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskStateAndTaskType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_state",
                columns: table => new
                {
                    task_state_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_state", x => x.task_state_id);
                });

            migrationBuilder.CreateTable(
                name: "task_type",
                columns: table => new
                {
                    task_type_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_type", x => x.task_type_id);
                });

            migrationBuilder.InsertData(
                table: "task_state",
                columns: new[] { "task_state_id", "name" },
                values: new object[,]
                {
                    { 1, "Ready" },
                    { 2, "InProgress" },
                    { 3, "OnHold" },
                    { 4, "Completed" },
                    { 5, "Failed" }
                });

            migrationBuilder.InsertData(
                table: "task_type",
                columns: new[] { "task_type_id", "name" },
                values: new object[] { 1, "BulkImport" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_state");

            migrationBuilder.DropTable(
                name: "task_type");
        }
    }
}
