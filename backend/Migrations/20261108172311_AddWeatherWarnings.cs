using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261108172311_AddWeatherWarnings : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "WeatherWarnings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Area = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                Source = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                AffectsTransit = table.Column<bool>(type: "bit", nullable: false),
                LastSentToTransitAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WeatherWarnings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WarningIncidentLinks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WeatherWarningId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WarningIncidentLinks", x => x.Id);
                table.ForeignKey(
                    name: "FK_WarningIncidentLinks_WeatherWarnings_WeatherWarningId",
                    column: x => x.WeatherWarningId,
                    principalTable: "WeatherWarnings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_WarningIncidentLinks_IncidentId",
            table: "WarningIncidentLinks",
            column: "IncidentId");

        migrationBuilder.CreateIndex(
            name: "IX_WarningIncidentLinks_WeatherWarningId_IncidentId",
            table: "WarningIncidentLinks",
            columns: new[] { "WeatherWarningId", "IncidentId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WeatherWarnings_StartsAt_EndsAt",
            table: "WeatherWarnings",
            columns: new[] { "StartsAt", "EndsAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WarningIncidentLinks");

        migrationBuilder.DropTable(
            name: "WeatherWarnings");
    }
}
