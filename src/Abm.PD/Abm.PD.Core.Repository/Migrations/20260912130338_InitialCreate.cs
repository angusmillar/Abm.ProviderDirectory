using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_data_source",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_data_source", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resource",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    resource_type = table.Column<string>(type: "text", nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "task",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    state_reason = table.Column<string>(type: "text", nullable: true),
                    trigger_every = table.Column<TimeSpan>(type: "interval", nullable: false),
                    to_start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    to_end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "export_loader_task_parameter",
                columns: table => new
                {
                    export_loader_task_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    type_filter_list = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_loader_task_parameter", x => x.export_loader_task_id);
                    table.ForeignKey(
                        name: "fk_export_loader_task_parameter_export_loader_tasks_export_loa",
                        column: x => x.export_loader_task_id,
                        principalTable: "task",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateIndex(
                name: "ix_provider_data_source_code",
                table: "provider_data_source",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_code",
                table: "task",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_loader_task_parameter");

            migrationBuilder.DropTable(
                name: "provider_data_source");

            migrationBuilder.DropTable(
                name: "resource");

            migrationBuilder.DropTable(
                name: "task_state");

            migrationBuilder.DropTable(
                name: "task_type");

            migrationBuilder.DropTable(
                name: "task");
        }
    }
}
