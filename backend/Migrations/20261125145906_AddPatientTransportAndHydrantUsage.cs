using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261125145906_AddPatientTransportAndHydrantUsage : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "HydrantUsages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HydrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LitresUsed = table.Column<double>(type: "float", nullable: false),
                SentToMaintenanceAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HydrantUsages", x => x.Id);
                table.CheckConstraint("CK_HydrantUsages_LitresUsed", "[LitresUsed] >= 0");
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
                TriageLevel = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
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
                    name: "FK_PatientTransports_Hospitals_HospitalId",
                    column: x => x.HospitalId,
                    principalTable: "Hospitals",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PatientTransports_Incidents_IncidentId",
                    column: x => x.IncidentId,
                    principalTable: "Incidents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_HydrantUsages_IncidentId",
            table: "HydrantUsages",
            column: "IncidentId");

        migrationBuilder.CreateIndex(
            name: "IX_HydrantUsages_SentToMaintenanceAt",
            table: "HydrantUsages",
            column: "SentToMaintenanceAt");

        migrationBuilder.CreateIndex(
            name: "IX_PatientTransports_HospitalId",
            table: "PatientTransports",
            column: "HospitalId");

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
    }
}
