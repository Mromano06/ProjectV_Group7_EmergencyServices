using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Data;

/// <summary>
/// Demo reference data (SH-D03): stations, units and hospitals, so a fresh
/// `docker compose up` has something to dispatch. Runs on startup when
/// Database:SeedOnStartup is true (set in docker-compose.yml).
///
/// - Only fills a table when it is EMPTY, so it never duplicates rows or overwrites real changes.
/// - Uses fixed IDs, so the same unit has the same ID on every fresh database (handy for demos and other teams).
/// - No incidents: those come from Security's 911 calls (SEC-FR-003) or dispatchers.
///
/// Names match the frontend's dispatch console (Station 1-5, Medic 2, Ladder 2, ...).
/// </summary>
public static class SeedData
{
    /// <summary>Who "created" seeded rows (SH-D04): an all-zero ID means "the system".</summary>
    public static readonly Guid SystemUser = Guid.Empty;

    // Fixed IDs – change only if you also reset every team's demo data.
    public static readonly Guid Station1 = new("5e11a000-0000-4000-8000-000000000001");
    public static readonly Guid Station2 = new("5e11a000-0000-4000-8000-000000000002");
    public static readonly Guid Station3 = new("5e11a000-0000-4000-8000-000000000003");
    public static readonly Guid Station4 = new("5e11a000-0000-4000-8000-000000000004");
    public static readonly Guid Station5 = new("5e11a000-0000-4000-8000-000000000005");

    /// <summary>Stations around Cambridge, ON (addresses are made up for the demo).</summary>
    public static IReadOnlyList<Station> Stations() =>
    [
        NewStation(Station1, "Station 1", StationType.Fire, 43.3601, -80.3123, "100 Main St, Cambridge, ON"),
        NewStation(Station2, "Station 2", StationType.EMS,  43.3920, -80.3170, "250 Hespeler Rd, Cambridge, ON"),
        NewStation(Station3, "Station 3", StationType.Fire, 43.4180, -80.3290, "75 Queen St E, Cambridge, ON"),
        NewStation(Station4, "Station 4", StationType.Fire, 43.3790, -80.3530, "40 Water St N, Cambridge, ON"),
        NewStation(Station5, "Station 5", StationType.EMS,  43.4005, -80.2810, "500 Franklin Blvd, Cambridge, ON"),
    ];

    /// <summary>Six units, one per row of the frontend's mock list (the duplicate "FireTruck 1" is now Engine 6).</summary>
    public static IReadOnlyList<Unit> Units() =>
    [
        NewUnit("5e11b000-0000-4000-8000-000000000001", "Engine 1", UnitType.Engine,    Station1),
        NewUnit("5e11b000-0000-4000-8000-000000000002", "Ladder 2", UnitType.Ladder,    Station4),
        NewUnit("5e11b000-0000-4000-8000-000000000003", "Engine 6", UnitType.Engine,    Station1),
        NewUnit("5e11b000-0000-4000-8000-000000000004", "Engine 3", UnitType.Engine,    Station3),
        NewUnit("5e11b000-0000-4000-8000-000000000005", "Medic 2",  UnitType.Ambulance, Station2),
        NewUnit("5e11b000-0000-4000-8000-000000000006", "Medic 5",  UnitType.Ambulance, Station5),
    ];

    /// <summary>The three hospitals the frontend shows, with their ER status and free beds (ES-FR-012).</summary>
    public static IReadOnlyList<Hospital> Hospitals() =>
    [
        NewHospital("5e11c000-0000-4000-8000-000000000001", "General Hospital",
            43.3790, -80.3080, "700 Coronation Blvd, Cambridge, ON", HospitalStatus.Open, beds: 18),
        NewHospital("5e11c000-0000-4000-8000-000000000002", "St. Mary's",
            43.4380, -80.5010, "911 Queen's Blvd, Kitchener, ON", HospitalStatus.Open, beds: 12),
        NewHospital("5e11c000-0000-4000-8000-000000000003", "Riverside Medical",
            43.3560, -80.3170, "15 Riverside Dr, Cambridge, ON", HospitalStatus.Diverting, beds: 2),
    ];

    /// <summary>
    /// Adds the demo data to empty tables. Safe to run on every startup.
    /// Stations and units go together (units need their stations).
    /// </summary>
    /// <returns>How many rows were added (0 when everything was already there).</returns>
    public static async Task<int> SeedAsync(EmergencyDbContext db, CancellationToken ct = default)
    {
        var added = 0;

        if (!await db.Stations.AnyAsync(ct) && !await db.Units.AnyAsync(ct))
        {
            var stations = Stations();
            var units = Units();
            db.Stations.AddRange(stations);
            db.Units.AddRange(units);
            added += stations.Count + units.Count;
        }

        if (!await db.Hospitals.AnyAsync(ct))
        {
            var hospitals = Hospitals();
            db.Hospitals.AddRange(hospitals);
            added += hospitals.Count;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    private static Station NewStation(Guid id, string name, StationType type,
        double lat, double lon, string address) => new()
    {
        Id = id, Name = name, Type = type, CreatedBy = SystemUser,
        Location = new Location { Latitude = lat, Longitude = lon, Address = address }
    };

    private static Unit NewUnit(string id, string callSign, UnitType type, Guid stationId) => new()
    {
        Id = new Guid(id), CallSign = callSign, Type = type, StationId = stationId,
        Status = UnitStatus.Available, CreatedBy = SystemUser
    };

    private static Hospital NewHospital(string id, string name, double lat, double lon, string address,
        HospitalStatus erStatus, int beds) => new()
    {
        Id = new Guid(id), Name = name, ErStatus = erStatus, AvailableBeds = beds, CreatedBy = SystemUser,
        Location = new Location { Latitude = lat, Longitude = lon, Address = address }
    };
}
