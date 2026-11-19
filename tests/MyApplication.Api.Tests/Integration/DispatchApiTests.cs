using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Assigning units to incidents, releasing them, and suggested units
/// (ES-FR-008, ES-FR-009, ES-NFR-006).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Dispatch")]
public class DispatchApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;

    public DispatchApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
    }

    private static string Assignments(Guid incidentId) => $"/api/emergency/v1/incidents/{incidentId}/assignments";

    // TC-API-DIS-01
    [Fact]
    [Trait("Requirement", "ES-FR-008")]
    public async Task AssigningAUnit_DispatchesBothTheUnitAndTheIncident()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();

        var assignments = await (await _dispatcher.AssignAsync(incident.Id, unit.Id))
            .ExpectAsync<List<AssignmentDto>>(HttpStatusCode.Created);

        Assert.Equal(unit.Id, Assert.Single(assignments).UnitId);
        var updatedIncident = await _dispatcher.GetFromJsonAsync<IncidentDetailDto>($"/api/emergency/v1/incidents/{incident.Id}");
        var updatedUnit = await _dispatcher.GetFromJsonAsync<UnitDto>($"/api/emergency/v1/units/{unit.Id}");
        Assert.Equal("Dispatched", updatedIncident!.Status);
        Assert.Equal("Dispatched", updatedUnit!.Status);
    }

    // TC-API-DIS-02
    [Fact]
    [Trait("Requirement", "ES-NFR-006")]
    public async Task AUnitAlreadyOnAnIncident_CannotBeAssignedToAnother_Returns409()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var first = await _dispatcher.CreateIncidentAsync();
        var second = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.AssignAsync(first.Id, unit.Id)).ExpectAsync(HttpStatusCode.Created);

        var response = await _dispatcher.AssignAsync(second.Id, unit.Id);

        Assert.Equal("unit_unavailable", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-DIS-03
    [Fact]
    [Trait("Requirement", "ES-NFR-006")]
    public async Task IfOneUnitIsUnavailable_NoneOfTheUnitsAreAssigned()
    {
        var station = await _admin.CreateStationAsync();
        var free = await _admin.CreateUnitAsync(station.Id);
        var busy = await _admin.CreateUnitAsync(station.Id);
        var other = await _dispatcher.CreateIncidentAsync();
        var incident = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.AssignAsync(other.Id, busy.Id)).ExpectAsync(HttpStatusCode.Created);

        var response = await _dispatcher.AssignAsync(incident.Id, free.Id, busy.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var freeUnit = await _dispatcher.GetFromJsonAsync<UnitDto>($"/api/emergency/v1/units/{free.Id}");
        Assert.Equal("Available", freeUnit!.Status); // rolled back, not left half-assigned
    }

    // TC-API-DIS-04
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task EmptyOrDuplicateUnitList_Returns400()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();

        var empty = await _dispatcher.AssignAsync(incident.Id);
        var duplicate = await _dispatcher.AssignAsync(incident.Id, unit.Id, unit.Id);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("invalid_units", await duplicate.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-DIS-05
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task AssigningToAnUnknownIncident_Returns404()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);

        var response = await _dispatcher.AssignAsync(Guid.NewGuid(), unit.Id);

        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-DIS-06
    [Fact]
    [Trait("Requirement", "ES-FR-008")]
    public async Task Responder_CannotAssignUnits_Gets403()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _responder.AssignAsync(incident.Id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-DIS-07
    [Fact]
    [Trait("Requirement", "ES-NFR-005")]
    public async Task ReleasingAUnit_FreesIt_AndKeepsTheAssignmentHistory()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();
        var assignment = (await (await _dispatcher.AssignAsync(incident.Id, unit.Id))
            .ExpectAsync<List<AssignmentDto>>(HttpStatusCode.Created)).Single();

        var release = await _dispatcher.PostAsync($"{Assignments(incident.Id)}/{assignment.Id}/release", null);
        var releaseAgain = await _dispatcher.PostAsync($"{Assignments(incident.Id)}/{assignment.Id}/release", null);

        var released = await release.ExpectAsync<AssignmentDto>(HttpStatusCode.OK);
        Assert.NotNull(released.ReleasedAt);
        Assert.Equal("already_released", await releaseAgain.ExpectErrorAsync(HttpStatusCode.Conflict));

        var history = await _dispatcher.GetFromJsonAsync<List<AssignmentDto>>(Assignments(incident.Id));
        Assert.Contains(history!, a => a.Id == assignment.Id); // not deleted
        var freedUnit = await _dispatcher.GetFromJsonAsync<UnitDto>($"/api/emergency/v1/units/{unit.Id}");
        Assert.Equal("Available", freedUnit!.Status);
    }

    // TC-API-DIS-08
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task ReleasingAnUnknownAssignment_Returns404()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.PostAsync($"{Assignments(incident.Id)}/{Guid.NewGuid()}/release", null);

        Assert.Equal("assignment_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-DIS-09
    [Fact]
    [Trait("Requirement", "ES-FR-009")]
    public async Task SuggestedUnits_AreAvailableUnitsOfThatType_NearestFirst()
    {
        // Incident in the city; one station ~1 km away, one ~20 km away.
        var near = await _admin.CreateStationAsync(lat: TestData.CityLat + 0.009);
        var far = await _admin.CreateStationAsync(lat: TestData.CityLat + 0.18);
        var farAmbulance = await _admin.CreateUnitAsync(far.Id, "Ambulance");
        var nearAmbulance = await _admin.CreateUnitAsync(near.Id, "Ambulance");
        var nearEngine = await _admin.CreateUnitAsync(near.Id, "Engine");
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");

        var suggestions = await (await _dispatcher.GetAsync(
                $"/api/emergency/v1/incidents/{incident.Id}/suggested-units?type=Ambulance"))
            .ExpectAsync<List<SuggestedUnitDto>>(HttpStatusCode.OK);

        var ids = suggestions.Select(s => s.Unit.Id).ToList();
        Assert.True(ids.IndexOf(nearAmbulance.Id) < ids.IndexOf(farAmbulance.Id), "Nearer ambulance should come first");
        Assert.DoesNotContain(nearEngine.Id, ids);
        Assert.All(suggestions, s => Assert.Equal("Ambulance", s.Unit.Type));
        Assert.Equal(suggestions.OrderBy(s => s.DistanceKm).Select(s => s.Unit.Id), ids);
    }

    // TC-API-DIS-10
    [Fact]
    [Trait("Requirement", "ES-FR-009")]
    public async Task SuggestedUnits_WithoutAType_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{incident.Id}/suggested-units");

        Assert.Equal("missing_type", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-DIS-11
    [Fact]
    [Trait("Requirement", "ES-FR-009")]
    public async Task BusyUnits_AreNotSuggested()
    {
        var station = await _admin.CreateStationAsync();
        var busy = await _admin.CreateUnitAsync(station.Id, "Ladder");
        var first = await _dispatcher.CreateIncidentAsync();
        var second = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.AssignAsync(first.Id, busy.Id)).ExpectAsync(HttpStatusCode.Created);

        var suggestions = await (await _dispatcher.GetAsync(
                $"/api/emergency/v1/incidents/{second.Id}/suggested-units?type=Ladder"))
            .ExpectAsync<List<SuggestedUnitDto>>(HttpStatusCode.OK);

        Assert.DoesNotContain(suggestions, s => s.Unit.Id == busy.Id);
    }
}
