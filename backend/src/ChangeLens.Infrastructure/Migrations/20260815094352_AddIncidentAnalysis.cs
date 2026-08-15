using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChangeLens.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                schema: "app",
                table: "analysis_runs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IncidentId",
                schema: "app",
                table: "analysis_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAtUtc",
                schema: "app",
                table: "analysis_runs",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                schema: "app",
                table: "analysis_runs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                schema: "app",
                table: "analysis_runs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultSchemaVersion",
                schema: "app",
                table: "analysis_runs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_analysis_runs_ProjectId_IncidentId",
                schema: "app",
                table: "analysis_runs",
                columns: new[] { "ProjectId", "IncidentId" });

            migrationBuilder.CreateIndex(
                name: "IX_analysis_runs_RequestId",
                schema: "app",
                table: "analysis_runs",
                column: "RequestId",
                unique: true,
                filter: "\"RequestId\" IS NOT NULL AND \"Status\" IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_analysis_runs_ProjectId_IncidentId",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropIndex(
                name: "IX_analysis_runs_RequestId",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "IncidentId",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                schema: "app",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "ResultSchemaVersion",
                schema: "app",
                table: "analysis_runs");
        }
    }
}
