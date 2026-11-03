using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_generation_tasks",
                columns: table => new
                {
                    task_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    engine = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    output_format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    result_file_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    result_storage_path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_generation_tasks", x => x.task_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_generation_tasks");
        }
    }
}
