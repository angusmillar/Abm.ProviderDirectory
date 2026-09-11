using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abm.PD.Core.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddExportLoaderTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "TaskBaseSequence");

            migrationBuilder.CreateTable(
                name: "export_loader_task",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false, defaultValueSql: "nextval('\"TaskBaseSequence\"')"),
                    code = table.Column<string>(type: "text", nullable: false),
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
                    last_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    parameter_type = table.Column<string>(type: "text", nullable: false),
                    parameter_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    parameter_type_filter_list = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_export_loader_task", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "export_loader_task");

            migrationBuilder.DropSequence(
                name: "TaskBaseSequence");
        }
    }
}
