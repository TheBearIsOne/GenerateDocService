using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "dispatch_attempts",
                table: "document_generation_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "dispatch_lock_id",
                table: "document_generation_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dispatch_lock_until_utc",
                table: "document_generation_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_message_json",
                table: "document_generation_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_document_generation_tasks_status_created_at_utc",
                table: "document_generation_tasks",
                columns: new[] { "status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_document_generation_tasks_status_created_at_utc",
                table: "document_generation_tasks");

            migrationBuilder.DropColumn(
                name: "dispatch_attempts",
                table: "document_generation_tasks");

            migrationBuilder.DropColumn(
                name: "dispatch_lock_id",
                table: "document_generation_tasks");

            migrationBuilder.DropColumn(
                name: "dispatch_lock_until_utc",
                table: "document_generation_tasks");

            migrationBuilder.DropColumn(
                name: "pending_message_json",
                table: "document_generation_tasks");
        }
    }
}
