using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261125163157_UpdateHospitalAndPatientTransport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_Hospitals_Capacity",
            table: "Hospitals");

        migrationBuilder.DropColumn(
            name: "Location",
            table: "Hospitals");

        migrationBuilder.DropColumn(
            name: "OccupiedRooms",
            table: "Hospitals");

        migrationBuilder.RenameColumn(
            name: "TotalRooms",
            table: "Hospitals",
            newName: "AvailableBeds");

        migrationBuilder.RenameColumn(
            name: "Status",
            table: "Hospitals",
            newName: "ErStatus");

        migrationBuilder.AddColumn<string>(
            name: "Address",
            table: "Hospitals",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "Latitude",
            table: "Hospitals",
            type: "float",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.AddColumn<double>(
            name: "Longitude",
            table: "Hospitals",
            type: "float",
            nullable: false,
            defaultValue: 0.0);

        migrationBuilder.CreateTable(
            name: "HydrantUsages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HydrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LitresUsed = table.Column<double>(type: "float", nullable: false),
                SentToMaintenanceAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                DeliveryAttempts = table.Column<int>(type: "int", nullable: false),
                LastAttempt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastDeliveryError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HydrantUsages", x => x.Id);
                table.CheckConstraint("CK_HydrantUsages_LitresUsed", "[LitresUsed] > 0");
                table.ForeignKey(
                    name: "FK_HydrantUsages_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PatientTransports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TriageLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                HospitalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ResidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PatientTransports", x => x.Id);
                table.ForeignKey(
                    name: "FK_PatientTransports_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "CK_Hospitals_AvailableBeds",
            table: "Hospitals",
            sql: "[AvailableBeds] >= 0");

        migrationBuilder.CreateIndex(
            name: "IX_HydrantUsages_IncidentId_SentToMaintenanceAt",
            table: "HydrantUsages",
            columns: new[] { "IncidentId", "SentToMaintenanceAt" });

        migrationBuilder.CreateIndex(
            name: "IX_PatientTransports_IncidentId",
            table: "PatientTransports",
            column: "IncidentId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "HydrantUsages");

        migrationBuilder.DropTable(
            name: "PatientTransports");

        migrationBuilder.DropCheckConstraint(
            name: "CK_Hospitals_AvailableBeds",
            table: "Hospitals");

        migrationBuilder.DropColumn(
            name: "Address",
            table: "Hospitals");

        migrationBuilder.DropColumn(
            name: "Latitude",
            table: "Hospitals");

        migrationBuilder.DropColumn(
            name: "Longitude",
            table: "Hospitals");

        migrationBuilder.RenameColumn(
            name: "ErStatus",
            table: "Hospitals",
            newName: "Status");

        migrationBuilder.RenameColumn(
            name: "AvailableBeds",
            table: "Hospitals",
            newName: "TotalRooms");

        migrationBuilder.AddColumn<string>(
            name: "Location",
            table: "Hospitals",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<int>(
            name: "OccupiedRooms",
            table: "Hospitals",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Hospitals_Capacity",
            table: "Hospitals",
            sql: "[TotalRooms] >= 0 AND [OccupiedRooms] >= 0 AND [OccupiedRooms] <= [TotalRooms]");
    }
}
