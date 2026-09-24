using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTaskAndResourceEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_task_parameter");

            migrationBuilder.DropTable(
                name: "resource");

            migrationBuilder.DropTable(
                name: "task_state");

            migrationBuilder.DropTable(
                name: "task_type");

            migrationBuilder.DropTable(
                name: "task");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resource",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    resource_id = table.Column<string>(type: "text", nullable: false),
                    resource_type = table.Column<string>(type: "text", nullable: false)
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
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    last_correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_run_count = table.Column<int>(type: "integer", nullable: true),
                    run_count = table.Column<int>(type: "integer", nullable: false),
                    start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    state_reason = table.Column<string>(type: "text", nullable: true),
                    trigger_every = table.Column<TimeSpan>(type: "interval", nullable: false),
                    type_id = table.Column<int>(type: "integer", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_source_id = table.Column<int>(type: "integer", nullable: true),
                    meta_data = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task", x => x.id);
                    table.ForeignKey(
                        name: "fk_task_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "export_task_parameter",
                columns: table => new
                {
                    export_task_id = table.Column<int>(type: "integer", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    type_filter_list = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_task_parameter", x => x.export_task_id);
                    table.ForeignKey(
                        name: "fk_export_task_parameter_task",
                        column: x => x.export_task_id,
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
                values: new object[,]
                {
                    { 1, "ExportTask" },
                    { 2, "SeedDirectoryTask" },
                    { 3, "ImportTask" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_code",
                table: "task",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_task_data_source_id",
                table: "task",
                column: "data_source_id");
        }
    }
}
