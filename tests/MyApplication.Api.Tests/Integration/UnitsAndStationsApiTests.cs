using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Units and stations reference data (ES-FR-007, ES-FR-011, Roles sheet: Admin manages them).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Units and stations")]
public class UnitsAndStationsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Units = "/api/emergency/v1/units";
    private const string Stations = "/api/emergency/v1/stations";

    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;
    private readonly HttpClient _viewer;

    public UnitsAndStationsApiTests(EmergencyApiFactory factory)
    {
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    // ---------- Stations ----------

    // TC-API-STN-01
    [Fact]
    [Trait("Requirement", "ES-FR-011")]
    public async Task Admin_CreatesStation_Gets201()
    {
        var response = await _admin.PostAsJsonAsync(Stations, new
        {
            name = "Station 7", type = "EMS", latitude = 43.46, longitude = -80.52, address = "7 Erb St"
        });

        var station = await response.ExpectAsync<StationDto>(HttpStatusCode.Created);
        Assert.Equal("EMS", station.Type);
        Assert.Contains(station.Id.ToString(), response.Headers.Location?.ToString());
    }

    // TC-API-STN-02
    [Fact]
    [Trait("Requirement", "ES-FR-011")]
    public async Task Dispatcher_CannotCreateStation_Gets403()
    {
        var response = await _dispatcher.PostAsJsonAsync(Stations, new
        {
            name = "Nope", type = "Fire", latitude = 43.46, longitude = -80.52
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-STN-03
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Station_WithBadCoordinatesOrNoType_Returns400()
    {
        var badCoords = await _admin.PostAsJsonAsync(Stations, new { name = "Bad", type = "Fire", latitude = 95.0, longitude = -80.0 });
        var noType = await _admin.PostAsJsonAsync(Stations, new { name = "Bad", latitude = 43.0, longitude = -80.0 });

        Assert.Equal("invalid_location", await badCoords.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("invalid_type", await noType.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-STN-04
    [Fact]
    [Trait("Requirement", "ES-FR-011")]
    public async Task Admin_UpdatesStation_AndResponderCanReadIt_WithItsUnits()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);

        var update = await _admin.PutAsJsonAsync($"{Stations}/{station.Id}", new
        {
            name = "Renamed Station", type = "Fire", latitude = station.Latitude, longitude = station.Longitude, address = "2 New St"
        });
        await update.ExpectAsync(HttpStatusCode.OK);

        var read = await (await _responder.GetAsync($"{Stations}/{station.Id}")).ExpectAsync<StationDto>(HttpStatusCode.OK);
        Assert.Equal("Renamed Station", read.Name);
        Assert.Contains(read.Units, u => u.Id == unit.Id);
    }

    // TC-API-STN-05
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownStation_Returns404()
    {
        var get = await _admin.GetAsync($"{Stations}/{Guid.NewGuid()}");
        var put = await _admin.PutAsJsonAsync($"{Stations}/{Guid.NewGuid()}", new
        {
            name = "Ghost", type = "Fire", latitude = 43.0, longitude = -80.0
        });

        Assert.Equal("station_not_found", await get.ExpectErrorAsync(HttpStatusCode.NotFound));
        Assert.Equal("station_not_found", await put.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-STN-06
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task Viewer_CannotSeeStations_Gets403()
    {
        var response = await _viewer.GetAsync(Stations);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Units ----------

    // TC-API-UNT-01
    [Fact]
    [Trait("Requirement", "ES-FR-007")]
    public async Task Admin_CreatesUnit_ItStartsAvailable()
    {
        var station = await _admin.CreateStationAsync();

        var unit = await _admin.CreateUnitAsync(station.Id, "Ladder");

        Assert.Equal("Available", unit.Status);
        Assert.Equal("Ladder", unit.Type);
        Assert.Equal(station.Id, unit.StationId);
    }

    // TC-API-UNT-02
    [Fact]
    [Trait("Requirement", "ES-FR-007")]
    public async Task DuplicateCallSign_Returns409()
    {
        var station = await _admin.CreateStationAsync();
        var callSign = TestData.UniqueCallSign();
        await (await _admin.PostAsJsonAsync(Units, new { callSign, type = "Engine", stationId = station.Id }))
            .ExpectAsync(HttpStatusCode.Created);

        var response = await _admin.PostAsJsonAsync(Units, new { callSign, type = "Engine", stationId = station.Id });

        Assert.Equal("duplicate_callsign", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-UNT-03
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Unit_AtUnknownStation_Returns400()
    {
        var response = await _admin.PostAsJsonAsync(Units, new
        {
            callSign = TestData.UniqueCallSign(), type = "Engine", stationId = Guid.NewGuid()
        });

        Assert.Equal("unknown_station", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-UNT-04
    [Fact]
    [Trait("Requirement", "ES-FR-007")]
    public async Task Dispatcher_CanListUnits_ButCannotCreateThem()
    {
        var station = await _admin.CreateStationAsync();

        var list = await _dispatcher.GetAsync($"{Units}?status=Available&type=Engine");
        var create = await _dispatcher.PostAsJsonAsync(Units, new
        {
            callSign = TestData.UniqueCallSign(), type = "Engine", stationId = station.Id
        });

        var page = await list.ExpectAsync<PagedResult<UnitDto>>(HttpStatusCode.OK);
        Assert.All(page.Items, u => { Assert.Equal("Available", u.Status); Assert.Equal("Engine", u.Type); });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    // TC-API-UNT-05
    [Fact]
    [Trait("Requirement", "ES-FR-008")]
    public async Task UnitCannotBeMarkedDispatched_WithoutAnAssignment_Returns409()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);

        var response = await _admin.PutAsJsonAsync($"{Units}/{unit.Id}", new
        {
            callSign = unit.CallSign, type = unit.Type, stationId = station.Id, status = "Dispatched"
        });

        Assert.Equal("assignment_required", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-UNT-06
    [Fact]
    [Trait("Requirement", "ES-FR-007")]
    public async Task Deactivating_SetsOutOfService_ButNotWhileOnAnIncident()
    {
        var station = await _admin.CreateStationAsync();
        var idle = await _admin.CreateUnitAsync(station.Id);
        var busy = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.AssignAsync(incident.Id, busy.Id)).ExpectAsync(HttpStatusCode.Created);

        var deactivated = await (await _admin.PostAsync($"{Units}/{idle.Id}/deactivate", null))
            .ExpectAsync<UnitDto>(HttpStatusCode.OK);
        var refused = await _admin.PostAsync($"{Units}/{busy.Id}/deactivate", null);

        Assert.Equal("OutOfService", deactivated.Status);
        Assert.Equal("unit_assigned", await refused.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-UNT-07
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownUnit_Returns404()
    {
        var response = await _dispatcher.GetAsync($"{Units}/{Guid.NewGuid()}");

        Assert.Equal("unit_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }
}
