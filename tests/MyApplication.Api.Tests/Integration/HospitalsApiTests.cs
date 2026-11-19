using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Controllers;
using MyApplication.Api.Enums;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Hospitals (read-only API today). The API has no endpoint to add hospitals yet,
/// so these tests put hospitals straight into the database first.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Hospitals")]
public class HospitalsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Url = "/api/emergency/v1/hospitals";

    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _responder;

    public HospitalsApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _responder = factory.CreateClientAs(Roles.Responder);
    }

    private async Task<Hospital> SeedHospitalAsync(int total, int occupied,
        HospitalStatus status = HospitalStatus.Operational)
    {
        var hospital = new Hospital
        {
            Name = "Test Hospital " + Guid.NewGuid().ToString("N")[..6],
            Location = "835 King St W, Kitchener",
            TotalRooms = total,
            OccupiedRooms = occupied,
            Status = status
        };
        await _factory.SeedAsync(db => db.Hospitals.Add(hospital));
        return hospital;
    }

    private async Task<(int Count, int Total, int Occupied, int Available)> GetSummaryAsync()
    {
        var json = await _responder.GetFromJsonAsync<JsonElement>($"{Url}/summary");
        return (json.GetProperty("hospitalCount").GetInt32(),
                json.GetProperty("totalRooms").GetInt32(),
                json.GetProperty("occupiedRooms").GetInt32(),
                json.GetProperty("availableRooms").GetInt32());
    }

    // TC-API-HOS-01
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Hospitals_AreListed_WithAvailableRoomsWorkedOut()
    {
        var hospital = await SeedHospitalAsync(total: 40, occupied: 31);

        var list = await (await _responder.GetAsync(Url)).ExpectAsync<List<HospitalDto>>(HttpStatusCode.OK);

        var found = Assert.Single(list, h => h.Id == hospital.Id);
        Assert.Equal(9, found.AvailableRooms);
        Assert.Equal("Operational", found.Status);
    }

    // TC-API-HOS-02
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task OneHospital_CanBeReadById()
    {
        var hospital = await SeedHospitalAsync(total: 10, occupied: 10, HospitalStatus.LimitedCapacity);

        var found = await (await _responder.GetAsync($"{Url}/{hospital.Id}")).ExpectAsync<HospitalDto>(HttpStatusCode.OK);

        Assert.Equal(hospital.Name, found.Name);
        Assert.Equal(0, found.AvailableRooms);
        Assert.Equal("LimitedCapacity", found.Status);
    }

    // TC-API-HOS-03
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownHospital_Returns404()
    {
        var response = await _responder.GetAsync($"{Url}/{Guid.NewGuid()}");

        Assert.Equal("hospital_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-HOS-04
    [Fact]
    [Trait("Requirement", "ES-FR-012")]
    public async Task Summary_AddsUpEveryHospital()
    {
        var before = await GetSummaryAsync();
        await SeedHospitalAsync(total: 20, occupied: 5);
        await SeedHospitalAsync(total: 30, occupied: 30);

        var after = await GetSummaryAsync();

        Assert.Equal(before.Count + 2, after.Count);
        Assert.Equal(before.Total + 50, after.Total);
        Assert.Equal(before.Occupied + 35, after.Occupied);
        Assert.Equal(before.Available + 15, after.Available);
    }

    // TC-API-HOS-05
    [Fact]
    [Trait("Requirement", "Data integrity")]
    public async Task Database_RejectsMoreOccupiedRoomsThanTotal()
    {
        // The CK_Hospitals_Capacity check constraint must stop bad data even if code forgets to.
        await Assert.ThrowsAnyAsync<Exception>(() => SeedHospitalAsync(total: 5, occupied: 6));
    }
}
