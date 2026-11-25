using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261125172830_UpdateHospitalModel : Migration
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

        // Old status names -> ER status (ES-FR-012), so hospitals in existing databases still load.
        migrationBuilder.Sql(
            "UPDATE Hospitals SET ErStatus = CASE ErStatus " +
            "WHEN 'Operational' THEN 'Open' WHEN 'LimitedCapacity' THEN 'Diverting' " +
            "WHEN 'Unavailable' THEN 'Closed' ELSE ErStatus END");

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

        migrationBuilder.AddCheckConstraint(
            name: "CK_Hospitals_AvailableBeds",
            table: "Hospitals",
            sql: "[AvailableBeds] >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.Sql(
            "UPDATE Hospitals SET Status = CASE Status " +
            "WHEN 'Open' THEN 'Operational' WHEN 'Diverting' THEN 'LimitedCapacity' " +
            "WHEN 'Closed' THEN 'Unavailable' ELSE Status END");

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
