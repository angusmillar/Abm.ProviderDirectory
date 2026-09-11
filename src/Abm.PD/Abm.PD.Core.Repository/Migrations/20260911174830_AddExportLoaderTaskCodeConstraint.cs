using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddExportLoaderTaskCodeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "export_loader_task",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateIndex(
                name: "ix_export_loader_task_code",
                table: "export_loader_task",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_export_loader_task_code",
                table: "export_loader_task");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "export_loader_task",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);
        }
    }
}
