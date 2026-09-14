using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskLastCorrelationIdAndRenameSourceResourceJobId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_source_resource_job_id_resource_type_resource_id",
                table: "source_resource");

            migrationBuilder.DropColumn(
                name: "job_id",
                table: "source_resource");

            migrationBuilder.AddColumn<Guid>(
                name: "last_correlation_id",
                table: "task",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "correlation_id",
                table: "source_resource",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.InsertData(
                table: "task_type",
                columns: new[] { "task_type_id", "name" },
                values: new object[] { 2, "MatchingTask" });

            migrationBuilder.CreateIndex(
                name: "ix_source_resource_correlation_id_resource_type_resource_id",
                table: "source_resource",
                columns: new[] { "correlation_id", "resource_type", "resource_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_source_resource_correlation_id_resource_type_resource_id",
                table: "source_resource");

            migrationBuilder.DeleteData(
                table: "task_type",
                keyColumn: "task_type_id",
                keyValue: 2);

            migrationBuilder.DropColumn(
                name: "last_correlation_id",
                table: "task");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "source_resource");

            migrationBuilder.AddColumn<string>(
                name: "job_id",
                table: "source_resource",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_source_resource_job_id_resource_type_resource_id",
                table: "source_resource",
                columns: new[] { "job_id", "resource_type", "resource_id" },
                unique: true);
        }
    }
}
