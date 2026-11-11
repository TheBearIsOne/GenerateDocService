using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GenerateDocService.DocumentProcessing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRedeliveryTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "message_sent_at_utc",
                table: "document_generation_tasks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "message_sent_at_utc",
                table: "document_generation_tasks");
        }
    }
}
