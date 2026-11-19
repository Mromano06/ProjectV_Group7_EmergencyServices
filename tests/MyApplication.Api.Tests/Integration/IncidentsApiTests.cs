using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Incidents: create, validation, roles, status rules, the live board, and a full
/// Reported -> Closed lifecycle (ES-FR-001 to ES-FR-008, ES-NFR-004/005, SH-C01).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Incidents")]
public class IncidentsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Url = "/api/emergency/v1/incidents";

    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;
    private readonly HttpClient _viewer;

    public IncidentsApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    // ---------- Create (ES-FR-001, ES-FR-002) ----------

    // TC-API-INC-01
    [Fact]
    [Trait("Requirement", "ES-FR-001")]
    public async Task Dispatcher_CreatesIncident_Gets201_StatusReported()
    {
        var response = await _dispatcher.PostAsJsonAsync(Url, TestData.NewIncident(type: "Medical", severity: "Critical"));

        var incident = await response.ExpectAsync<IncidentDetailDto>(HttpStatusCode.Created);
        Assert.Equal("Reported", incident.Status);
        Assert.Equal("Medical", incident.Type);
        Assert.Equal(1, incident.Severity);
        Assert.Equal("100 King St W", incident.Location.Address);
        Assert.Contains(incident.Id.ToString(), response.Headers.Location?.ToString());
    }

    // TC-API-INC-02
    [Fact]
    [Trait("Requirement", "SH-C01")]
    public async Task SecurityServiceAccount_CanForwardA911Call()
    {
        var security = _factory.CreateServiceClient("security");

        var response = await security.PostAsJsonAsync(Url, TestData.NewIncident(sourceCallId: Guid.NewGuid()));

        await response.ExpectAsync(HttpStatusCode.Created);
    }

    // TC-API-INC-03
    [Theory]
    [InlineData(Roles.Responder)]
    [InlineData(Roles.Viewer)]
    [Trait("Requirement", "ES-FR-001")]
    public async Task OtherRoles_CannotCreateIncidents_Get403(string role)
    {
        var client = _factory.CreateClientAs(role);

        var response = await client.PostAsJsonAsync(Url, TestData.NewIncident());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-INC-04
    [Fact]
    [Trait("Requirement", "ES-FR-002")]
    public async Task MissingLocation_Returns400()
    {
        var response = await _dispatcher.PostAsJsonAsync(Url, new { type = "Fire", severity = "High" });

        Assert.Equal("validation_failed", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-INC-05
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task InvalidCoordinates_Returns400()
    {
        var response = await _dispatcher.PostAsJsonAsync(Url, TestData.NewIncident(lat: 200));

        Assert.Equal("invalid_location", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-INC-06
    [Fact]
    [Trait("Requirement", "ES-FR-002")]
    public async Task MissingTypeOrSeverity_Returns400()
    {
        var response = await _dispatcher.PostAsJsonAsync(Url, new
        {
            location = new { latitude = TestData.CityLat, longitude = TestData.CityLon, address = "1 Main St" }
        });

        Assert.Equal("invalid_type_or_severity", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-INC-07
    [Fact]
    [Trait("Requirement", "SH-C01")]
    public async Task SameSecurityCallTwice_Returns409()
    {
        var callId = Guid.NewGuid();
        await _dispatcher.CreateIncidentAsync(sourceCallId: callId);

        var response = await _dispatcher.PostAsJsonAsync(Url, TestData.NewIncident(sourceCallId: callId));

        Assert.Equal("duplicate_source_call", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // ---------- Read ----------

    // TC-API-INC-08
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownIncident_Returns404()
    {
        var response = await _dispatcher.GetAsync($"{Url}/{Guid.NewGuid()}");

        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-INC-09
    [Fact]
    [Trait("Requirement", "ES-NFR-007")]
    public async Task Viewer_CannotSeeIncidentsOrCallerDetails_Gets403()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var list = await _viewer.GetAsync(Url);
        var detail = await _viewer.GetAsync($"{Url}/{incident.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    // TC-API-INC-10
    [Fact]
    [Trait("Requirement", "ES-FR-004")]
    public async Task LiveBoard_SortsBySeverityThenAge()
    {
        var low = await _dispatcher.CreateIncidentAsync(severity: "Low");
        var critical = await _dispatcher.CreateIncidentAsync(severity: "Critical");

        var board = await (await _dispatcher.GetAsync($"{Url}?pageSize=100"))
            .ExpectAsync<PagedResult<IncidentSummaryDto>>(HttpStatusCode.OK);

        var ids = board.Items.Select(i => i.Id).ToList();
        Assert.True(ids.IndexOf(critical.Id) < ids.IndexOf(low.Id), "Critical incident should be listed before Low");
        Assert.Equal(board.Items.OrderBy(i => i.Severity).Select(i => i.Severity), board.Items.Select(i => i.Severity));
    }

    // ---------- Status rules (ES-FR-003) ----------

    // TC-API-INC-11
    [Fact]
    [Trait("Requirement", "ES-FR-006")]
    public async Task JumpingStraightToClosed_Returns409()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.ChangeStatusAsync(incident.Id, "Closed");

        Assert.Equal("report_required", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-INC-12
    [Fact]
    [Trait("Requirement", "ES-FR-003")]
    public async Task SkippingAStatus_Returns409()
    {
        var incident = await _dispatcher.CreateIncidentAsync(); // Reported

        var response = await _dispatcher.ChangeStatusAsync(incident.Id, "OnScene");

        Assert.Equal("invalid_status_transition", await response.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-INC-13
    [Fact]
    [Trait("Requirement", "ES-FR-003")]
    public async Task UnknownStatus_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.ChangeStatusAsync(incident.Id, "Teleported");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Full lifecycle ----------

    // TC-API-INC-14
    [Fact]
    [Trait("Requirement", "ES-FR-003")]
    public async Task FullLifecycle_ReportedToClosed_RecordsEveryStepAndFreesTheUnit()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();

        await _dispatcher.ResolveAsync(incident.Id, unit.Id);
        var closed = await (await _responder.FileReportAsync(incident.Id, deaths: 1, injuries: 2))
            .ExpectAsync<IncidentDetailDto>(HttpStatusCode.OK);

        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);
        Assert.Equal(1, closed.Report!.Deaths);
        Assert.Equal(
            new[] { "Reported>Dispatched", "Dispatched>EnRoute", "EnRoute>OnScene", "OnScene>Resolved", "Resolved>Closed" },
            closed.History.Select(h => $"{h.FromStatus}>{h.ToStatus}"));
        Assert.All(closed.History, h => Assert.Equal(TestAuthHandler.UserId, h.ChangedBy));

        var assignment = Assert.Single(closed.Assignments);
        Assert.NotNull(assignment.ArrivedAt);
        Assert.NotNull(assignment.ReleasedAt);

        var freedUnit = await (await _dispatcher.GetAsync($"/api/emergency/v1/units/{unit.Id}"))
            .ExpectAsync<UnitDto>(HttpStatusCode.OK);
        Assert.Equal("Available", freedUnit.Status);
    }

    // TC-API-INC-15
    [Fact]
    [Trait("Requirement", "ES-FR-003")]
    public async Task ClosedIncidents_LeaveTheLiveBoard_AndCannotBeDispatchedTo()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var otherUnit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();
        await _dispatcher.ResolveAsync(incident.Id, unit.Id);
        await (await _responder.FileReportAsync(incident.Id)).ExpectAsync(HttpStatusCode.OK);

        var board = await (await _dispatcher.GetAsync($"{Url}?pageSize=100"))
            .ExpectAsync<PagedResult<IncidentSummaryDto>>(HttpStatusCode.OK);
        var assign = await _dispatcher.AssignAsync(incident.Id, otherUnit.Id);

        Assert.DoesNotContain(board.Items, i => i.Id == incident.Id);
        Assert.Equal("incident_inactive", await assign.ExpectErrorAsync(HttpStatusCode.Conflict));
    }

    // TC-API-INC-16
    [Fact]
    [Trait("Requirement", "ES-FR-006")]
    public async Task Report_BeforeResolved_OrTwice_Returns409()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();

        var tooEarly = await _responder.FileReportAsync(incident.Id);
        await _dispatcher.ResolveAsync(incident.Id, unit.Id);
        await (await _responder.FileReportAsync(incident.Id)).ExpectAsync(HttpStatusCode.OK);
        var twice = await _responder.FileReportAsync(incident.Id);

        Assert.Equal("incident_not_resolved", await tooEarly.ExpectErrorAsync(HttpStatusCode.Conflict));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
    }

    // TC-API-INC-17
    [Fact]
    [Trait("Requirement", "ES-FR-006")]
    public async Task Dispatcher_CannotFileAfterActionReport_Gets403()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.FileReportAsync(incident.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-INC-18
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Report_WithNegativeDeaths_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _responder.FileReportAsync(incident.Id, deaths: -1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Dates (SH-I04) ----------

    // TC-API-INC-19
    [Fact]
    [Trait("Requirement", "SH-I04")]
    public async Task AllIncidentDates_AreSentAsUtc()
    {
        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id);
        var incident = await _dispatcher.CreateIncidentAsync();
        await _dispatcher.ResolveAsync(incident.Id, unit.Id);
        await (await _responder.FileReportAsync(incident.Id)).ExpectAsync(HttpStatusCode.OK);

        // Read the raw JSON: every date must end in "Z", or the frontend will show it in the wrong time zone.
        var json = await _dispatcher.GetStringAsync($"{Url}/{incident.Id}");
        var notUtc = FindNonUtcDates(JsonDocument.Parse(json).RootElement, "$").ToList();

        Assert.True(notUtc.Count == 0,
            "These dates are missing the 'Z' (UTC) marker (SH-I04):\n  " + string.Join("\n  ", notUtc));
    }

    /// <summary>Walks the JSON and lists every date-like string that doesn't end in Z.</summary>
    internal static IEnumerable<string> FindNonUtcDates(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                    foreach (var found in FindNonUtcDates(p.Value, $"{path}.{p.Name}"))
                        yield return found;
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                    foreach (var found in FindNonUtcDates(item, $"{path}[{i++}]"))
                        yield return found;
                break;
            case JsonValueKind.String:
                var text = element.GetString()!;
                if (text.Length >= 19 && text[4] == '-' && text[10] == 'T' && DateTime.TryParse(text, out _)
                    && !text.EndsWith('Z') && !text.EndsWith("+00:00"))
                    yield return $"{path} = {text}";
                break;
        }
    }
}
