using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;
using MyApplication.Api.Tests.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// Demo seed data (SH-D03): a fresh database gets stations, units and hospitals,
/// and running the seeder again never duplicates or overwrites anything.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Seed data")]
public sealed class SeedDataTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:;Foreign Keys=True");

    public SeedDataTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    /// <summary>A new context on the same empty in-memory database (tables created once).</summary>
    private EmergencyDbContext NewContext()
    {
        var db = new SqliteTestDbContext(new DbContextOptionsBuilder<EmergencyDbContext>()
            .UseSqlite(_connection).Options);
        db.Database.EnsureCreated();
        return db;
    }

    // TC-SEED-01
    [Fact]
    [Trait("Requirement", "SH-D03")]
    public async Task FreshDatabase_GetsStationsUnitsAndHospitals()
    {
        using (var db = NewContext()) await SeedData.SeedAsync(db);

        using var check = NewContext();
        Assert.Equal(5, await check.Stations.CountAsync());
        Assert.Equal(6, await check.Units.CountAsync());
        Assert.Equal(3, await check.Hospitals.CountAsync());
        Assert.All(await check.Units.ToListAsync(), u => Assert.Equal(UnitStatus.Available, u.Status));
        Assert.True(await check.Units.AnyAsync(u => u.Type == UnitType.Engine));
        Assert.True(await check.Units.AnyAsync(u => u.Type == UnitType.Ladder));
        Assert.True(await check.Units.AnyAsync(u => u.Type == UnitType.Ambulance));
        Assert.False(await check.Incidents.AnyAsync()); // incidents come from Security / dispatchers
    }

    // TC-SEED-02
    [Fact]
    [Trait("Requirement", "SH-D03")]
    public async Task RunningTheSeederAgain_AddsNothing()
    {
        using (var db = NewContext()) await SeedData.SeedAsync(db);

        int addedSecondTime;
        using (var db = NewContext()) addedSecondTime = await SeedData.SeedAsync(db);

        using var check = NewContext();
        Assert.Equal(0, addedSecondTime);
        Assert.Equal(5, await check.Stations.CountAsync());
        Assert.Equal(6, await check.Units.CountAsync());
        Assert.Equal(3, await check.Hospitals.CountAsync());
    }

    // TC-SEED-03
    [Fact]
    [Trait("Requirement", "SH-D03")]
    public async Task ExistingData_IsLeftAlone()
    {
        // A team member already added their own station and hospital.
        using (var db = NewContext())
        {
            db.Stations.Add(new Station
            {
                Name = "My Station", Type = StationType.Fire,
                Location = new Location { Latitude = 43.4, Longitude = -80.3, Address = "1 Test St" }
            });
            db.Hospitals.Add(new Hospital
            {
                Name = "My Hospital", ErStatus = HospitalStatus.Open, AvailableBeds = 5,
                Location = new Location { Latitude = 43.4, Longitude = -80.3, Address = "2 Test St" }
            });
            await db.SaveChangesAsync();
        }

        int added;
        using (var db = NewContext()) added = await SeedData.SeedAsync(db);

        using var check = NewContext();
        Assert.Equal(0, added);
        Assert.Equal("My Station", (await check.Stations.SingleAsync()).Name);
        Assert.Equal("My Hospital", (await check.Hospitals.SingleAsync()).Name);
        Assert.False(await check.Units.AnyAsync());
    }

    // TC-SEED-04
    [Fact]
    [Trait("Requirement", "SH-D03")]
    public void SeedData_IsConsistent()
    {
        var stations = SeedData.Stations().ToDictionary(s => s.Id);
        var units = SeedData.Units();

        // Unique call signs (the database requires it) and IDs.
        Assert.Equal(units.Count, units.Select(u => u.CallSign).Distinct().Count());
        Assert.Equal(units.Count, units.Select(u => u.Id).Distinct().Count());

        // Every unit's home station exists; ambulances live at EMS stations, the rest at fire stations.
        Assert.All(units, u =>
        {
            Assert.True(stations.ContainsKey(u.StationId), $"{u.CallSign} has an unknown station");
            var expected = u.Type == UnitType.Ambulance ? StationType.EMS : StationType.Fire;
            Assert.Equal(expected, stations[u.StationId].Type);
        });

        // Hospitals respect CK_Hospitals_AvailableBeds, have real coordinates, and at least one ER
        // is Open so "nearest open hospital" (ES-FR-013) has an answer in the demo.
        var hospitals = SeedData.Hospitals();
        Assert.All(hospitals, h => Assert.True(h.AvailableBeds >= 0));
        Assert.All(hospitals, h => Assert.InRange(h.Location.Latitude, 43.0, 44.0));
        Assert.Contains(hospitals, h => h.ErStatus == HospitalStatus.Open);
    }
}
