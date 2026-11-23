using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helinho.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiQuestionDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AiEditedAt",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AiJobId",
                table: "Questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GeneratedAt",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Questions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Team");

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "Questions",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiGenerationJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuizType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AgeBand = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedCount = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PromptVersion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    GeneratedCount = table.Column<int>(type: "integer", nullable: false),
                    ValidCount = table.Column<int>(type: "integer", nullable: false),
                    InvalidCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiGenerationJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiQuestionSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Statement = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OptionA = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OptionB = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OptionC = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OptionD = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CorrectIndex = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContentPurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiQuestionSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiQuestionSuggestions_AiGenerationJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "AiGenerationJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AiQuestionSuggestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_AiJobId",
                table: "Questions",
                column: "AiJobId");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationJobs_CreatedAt",
                table: "AiGenerationJobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiGenerationJobs_RequestedByUserId_CreatedAt",
                table: "AiGenerationJobs",
                columns: new[] { "RequestedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiQuestionSuggestions_JobId_Position",
                table: "AiQuestionSuggestions",
                columns: new[] { "JobId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiQuestionSuggestions_QuestionId",
                table: "AiQuestionSuggestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiQuestionSuggestions_Status",
                table: "AiQuestionSuggestions",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_AiGenerationJobs_AiJobId",
                table: "Questions",
                column: "AiJobId",
                principalTable: "AiGenerationJobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_AiGenerationJobs_AiJobId",
                table: "Questions");

            migrationBuilder.DropTable(
                name: "AiQuestionSuggestions");

            migrationBuilder.DropTable(
                name: "AiGenerationJobs");

            migrationBuilder.DropIndex(
                name: "IX_Questions_AiJobId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "AiEditedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "AiJobId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "GeneratedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "Questions");
        }
    }
}
