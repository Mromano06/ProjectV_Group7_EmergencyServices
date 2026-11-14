using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261114180943_AddEmergencyOperations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Incidents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Severity = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Latitude = table.Column<double>(type: "float", nullable: false),
                Longitude = table.Column<double>(type: "float", nullable: false),
                Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                CallerName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                CallerPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                SourceCallId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ReportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Incidents", x => x.Id);
                table.CheckConstraint("CK_Incidents_Severity", "[Severity] BETWEEN 1 AND 4");
            });

        migrationBuilder.CreateTable(
            name: "Stations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Latitude = table.Column<double>(type: "float", nullable: false),
                Longitude = table.Column<double>(type: "float", nullable: false),
                Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Stations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "AfterActionReports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Injuries = table.Column<int>(type: "int", nullable: false),
                Deaths = table.Column<int>(type: "int", nullable: false),
                HydrantWaterLitres = table.Column<double>(type: "float", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                FiledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FiledAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AfterActionReports", x => x.Id);
                table.CheckConstraint("CK_AAR_Nonnegative", "[Injuries] >= 0 AND [Deaths] >= 0 AND [HydrantWaterLitres] >= 0");
                table.ForeignKey(
                    name: "FK_AfterActionReports_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IncidentStatusHistories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                FromStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                ToStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                ChangedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IncidentStatusHistories", x => x.Id);
                table.ForeignKey(
                    name: "FK_IncidentStatusHistories_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Units",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CallSign = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                StationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Units", x => x.Id);
                table.ForeignKey(
                    name: "FK_Units_Stations_StationId",
                    column: x => x.StationId,
                    principalTable: "Stations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IncidentAssignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                ArrivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IncidentAssignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_IncidentAssignments_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_IncidentAssignments_Units_UnitId",
                    column: x => x.UnitId,
                    principalTable: "Units",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AfterActionReports_IncidentId",
            table: "AfterActionReports",
            column: "IncidentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_IncidentAssignments_IncidentId",
            table: "IncidentAssignments",
            column: "IncidentId");

        migrationBuilder.CreateIndex(
            name: "IX_IncidentAssignments_UnitId",
            table: "IncidentAssignments",
            column: "UnitId",
            unique: true,
            filter: "[ReleasedAt] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_SourceCallId",
            table: "Incidents",
            column: "SourceCallId",
            unique: true,
            filter: "[SourceCallId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Incidents_Status_Severity_ReportedAt",
            table: "Incidents",
            columns: new[] { "Status", "Severity", "ReportedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_IncidentStatusHistories_IncidentId_ChangedAt",
            table: "IncidentStatusHistories",
            columns: new[] { "IncidentId", "ChangedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_Units_CallSign",
            table: "Units",
            column: "CallSign",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Units_StationId",
            table: "Units",
            column: "StationId");

        migrationBuilder.CreateIndex(
            name: "IX_Units_Status_Type",
            table: "Units",
            columns: new[] { "Status", "Type" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AfterActionReports");

        migrationBuilder.DropTable(
            name: "IncidentAssignments");

        migrationBuilder.DropTable(
            name: "IncidentStatusHistories");

        migrationBuilder.DropTable(
            name: "Units");

        migrationBuilder.DropTable(
            name: "Incidents");

        migrationBuilder.DropTable(
            name: "Stations");
    }
}
