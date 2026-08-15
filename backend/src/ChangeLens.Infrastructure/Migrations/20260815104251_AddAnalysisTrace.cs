using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChangeLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TraceJson",
                schema: "app",
                table: "analysis_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceSchemaVersion",
                schema: "app",
                table: "analysis_runs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceJson",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "TraceSchemaVersion",
                schema: "app",
                table: "analysis_runs");
        }
    }
}
