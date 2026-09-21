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
                name: "data_source",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_source", x => x.id);
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
                name: "source_resource",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_last_updated = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data_source_id = table.Column<int>(type: "integer", nullable: false),
                    resource = table.Column<string>(type: "jsonb", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_resource", x => x.id);
                    table.ForeignKey(
                        name: "fk_source_resource_data_source_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    run_count = table.Column<int>(type: "integer", nullable: false),
                    max_run_count = table.Column<int>(type: "integer", nullable: true),
                    last_correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                name: "export_task_parameter",
                columns: table => new
                {
                    export_task_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    { 2, "MatchingTask" },
                    { 3, "ImportTask" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_source_code",
                table: "data_source",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_resource_correlation_id_resource_type_resource_id",
                table: "source_resource",
                columns: new[] { "correlation_id", "resource_type", "resource_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_resource_data_source_id",
                table: "source_resource",
                column: "data_source_id");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_task_parameter");

            migrationBuilder.DropTable(
                name: "resource");

            migrationBuilder.DropTable(
                name: "source_resource");

            migrationBuilder.DropTable(
                name: "task_state");

            migrationBuilder.DropTable(
                name: "task_type");

            migrationBuilder.DropTable(
                name: "task");

            migrationBuilder.DropTable(
                name: "data_source");
        }
    }
}
