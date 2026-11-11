using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyApplication.Api.Migrations;

/// <inheritdoc />
public partial class _20261109170510_AddHospitals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Hospitals",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                TotalRooms = table.Column<int>(type: "int", nullable: false),
                OccupiedRooms = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Hospitals", x => x.Id);
                table.CheckConstraint("CK_Hospitals_Capacity", "[TotalRooms] >= 0 AND [OccupiedRooms] >= 0 AND [OccupiedRooms] <= [TotalRooms]");
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Hospitals");
    }
}
