using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// ES-FR-015 / SH-C13 / PT-FR-019: marking an incident as road-restricting tells Public Transport
/// (POST /api/transport/v1/integrations/emergency/incidents, their Swagger). Sending again updates it
/// and DELETE clears it, always with the same incident ID (external_id).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "External services")]
public class RoadRestrictionApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _viewer;

    public RoadRestrictionApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    private static string Url(Guid incidentId) => $"/api/emergency/v1/incidents/{incidentId}/road-restriction";

    private static object Restriction(DateTime? endsAt = null) => new
    {
        affectedArea = "King St W between Victoria and Water",
        restrictionType = "Full closure",
        estimatedEndAt = endsAt ?? DateTime.UtcNow.AddHours(3)
    };

    private List<TransitEmergencyIncident> SentFor(Guid incidentId) =>
        _factory.Transit.SentIncidents.Where(n => n.ExternalId == incidentId.ToString()).ToList();

    // TC-API-RR-01
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task Dispatcher_MarksIncidentRoadRestricting_AndTransitGetsTheIncident()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Fire", severity: "High"); // "100 King St W"
        var endsAt = DateTime.UtcNow.AddHours(3);

        var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction(endsAt));

        var result = await response.ExpectAsync<RoadRestrictionResult>(HttpStatusCode.OK);
        Assert.Equal("active", result.Status);
        var sent = Assert.Single(SentFor(incident.Id));
        Assert.Equal("road_closure", sent.Type);
        Assert.Equal("fire", sent.IncidentType);
        Assert.Equal("high", sent.Severity);
        Assert.Equal("active", sent.Status);
        Assert.Equal("King St W between Victoria and Water", sent.AffectedArea);   // location + affected area
        Assert.Equal(new[] { "King St W" }, sent.AffectedSegments);
        Assert.Contains("Full closure", sent.Title);                               // restriction type
        Assert.Equal(TestData.CityLat, sent.Latitude);
        Assert.Equal(TestData.CityLon, sent.Longitude);
        Assert.Equal(TransitEmergencyIncident.Rfc3339(endsAt), sent.EndTime);      // estimated end, UTC (SH-I04)
        Assert.EndsWith("Z", sent.StartTime);
    }

    // TC-API-RR-08
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task SendingAgain_UpdatesTheSameIncidentAtTransit()
    {
        var incident = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction())).ExpectAsync(HttpStatusCode.OK);

        var later = DateTime.UtcNow.AddHours(6);
        await (await _dispatcher.PostAsJsonAsync(Url(incident.Id), new
        {
            affectedArea = "King St W at Water St only",
            restrictionType = "Lane closure",
            estimatedEndAt = later
        })).ExpectAsync(HttpStatusCode.OK);

        var sent = SentFor(incident.Id);
        Assert.Equal(2, sent.Count);                                   // same external_id both times
        Assert.Equal("King St W at Water St only", sent[1].AffectedArea);
        Assert.Contains("Lane closure", sent[1].Title);
        Assert.Equal(TransitEmergencyIncident.Rfc3339(later), sent[1].EndTime);
    }

    // TC-API-RR-09
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task ClearingTheRestriction_SendsTheSameIncidentAsResolved()
    {
        var incident = await _dispatcher.CreateIncidentAsync();
        await (await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction())).ExpectAsync(HttpStatusCode.OK);

        var result = await (await _dispatcher.DeleteAsync(Url(incident.Id)))
            .ExpectAsync<RoadRestrictionResult>(HttpStatusCode.OK);

        Assert.Equal("resolved", result.Status);
        var cleared = SentFor(incident.Id).Last();
        Assert.Equal("resolved", cleared.Status);
        Assert.True(DateTime.Parse(cleared.EndTime).ToUniversalTime() <= DateTime.UtcNow.AddSeconds(5)); // open again now
    }

    // TC-API-RR-10
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task ClearingForAnUnknownIncident_Returns404()
    {
        var response = await _dispatcher.DeleteAsync(Url(Guid.NewGuid()));

        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-RR-02
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task WhenTransitIsDown_Returns502_WithAClearMessage()
    {
        var incident = await _dispatcher.CreateIncidentAsync();
        _factory.Transit.ShouldSucceed = false;
        try
        {
            var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction());

            Assert.Equal("transit_unavailable", await response.ExpectErrorAsync(HttpStatusCode.BadGateway));
        }
        finally
        {
            _factory.Transit.ShouldSucceed = true;
        }
    }

    // TC-API-RR-03
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task EndTimeInThePast_Returns400_AndNothingIsSent()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction(DateTime.UtcNow.AddMinutes(-5)));

        Assert.Equal("ends_in_past", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Empty(SentFor(incident.Id));
    }

    // TC-API-RR-04
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task MissingAreaOrType_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id),
            new { estimatedEndAt = DateTime.UtcNow.AddHours(1) });

        Assert.Equal("validation_failed", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-RR-05
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownIncident_Returns404()
    {
        var response = await _dispatcher.PostAsJsonAsync(Url(Guid.NewGuid()), Restriction());

        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-RR-06
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task ResolvedIncident_Returns409()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();
        await _dispatcher.ResolveAsync(incident.Id, unit.Id);

        var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction());

        Assert.Equal("incident_inactive", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-RR-07
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task Viewer_CannotSendRestrictions()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _viewer.PostAsJsonAsync(Url(incident.Id), Restriction());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
