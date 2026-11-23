using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helinho.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueueJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Feedback = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    FeedbackAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueJobs_ExternalId",
                table: "QueueJobs",
                column: "ExternalId",
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QueueJobs_Type_NextAttemptAt",
                table: "QueueJobs",
                columns: new[] { "Type", "NextAttemptAt" },
                filter: "\"Status\" IN ('Pending', 'Processing')");

            migrationBuilder.CreateIndex(
                name: "IX_QueueJobs_Type_SubjectId_CreatedAt",
                table: "QueueJobs",
                columns: new[] { "Type", "SubjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_QueueJobs_IdempotencyKey",
                table: "QueueJobs",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueJobs");
        }
    }
}
