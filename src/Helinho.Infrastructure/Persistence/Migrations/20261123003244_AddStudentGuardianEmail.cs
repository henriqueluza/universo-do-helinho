using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helinho.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentGuardianEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuardianEmail",
                table: "Students",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousGuardianEmail",
                table: "Students",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuardianEmail",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "PreviousGuardianEmail",
                table: "Students");
        }
    }
}
