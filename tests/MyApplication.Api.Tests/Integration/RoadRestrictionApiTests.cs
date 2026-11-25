using System.Net;
using System.Net.Http.Json;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// ES-FR-015 / SH-C13: marking an incident as road-restricting sends a notice to Public Transport.
/// Transit asked for one notice per incident (no updates or clears).
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

    // TC-API-RR-01
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task Dispatcher_MarksIncidentRoadRestricting_AndTransitGetsTheNotice()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Fire");

        var response = await _dispatcher.PostAsJsonAsync(Url(incident.Id), Restriction());

        await response.ExpectAsync(HttpStatusCode.OK);
        var notice = Assert.Single(_factory.Transit.SentIncidents, n => n.IncidentId == incident.Id);
        Assert.Equal("Fire", notice.IncidentType);
        Assert.Equal(TestData.CityLat, notice.Latitude);
        Assert.Equal(TestData.CityLon, notice.Longitude);
        Assert.Equal("100 King St W", notice.Address);
        Assert.Equal("King St W between Victoria and Water", notice.AffectedArea);
        Assert.Equal("Full closure", notice.RestrictionType);
        Assert.Equal(DateTimeKind.Utc, notice.EstimatedEndAt.Kind); // SH-I04
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
        Assert.DoesNotContain(_factory.Transit.SentIncidents, n => n.IncidentId == incident.Id);
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
