using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// ES-FR-014: patients transported per incident – triage level and destination hospital only,
/// never medical history.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Patients and hydrants")]
public class PatientTransportApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;
    private readonly HttpClient _viewer;

    public PatientTransportApiTests(EmergencyApiFactory factory)
    {
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    private static string Url(Guid incidentId) => $"/api/emergency/v1/incidents/{incidentId}/patient-transports";

    private async Task<Guid> CreateHospitalAsync()
    {
        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/hospitals", new
        {
            name = "Transport Test Hospital " + Guid.NewGuid().ToString("N")[..6],
            location = new { latitude = 43.39, longitude = -80.31, address = "1 Hospital Rd" },
            erStatus = "Open",
            availableBeds = 5
        });
        return (await response.ExpectAsync<HospitalDto>(HttpStatusCode.Created)).Id;
    }

    // TC-API-PT-01
    [Fact]
    [Trait("Requirement", "ES-FR-014")]
    public async Task Responder_RecordsATransport_AndItCanBeListedAndRead()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
        var hospitalId = await CreateHospitalAsync();
        var residentId = Guid.NewGuid();

        var response = await _responder.PostAsJsonAsync(Url(incident.Id),
            new { triageLevel = "Urgent", hospitalId, residentId });

        var created = await response.ExpectAsync<JsonElement>(HttpStatusCode.Created);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("Urgent", created.GetProperty("triageLevel").GetString());
        Assert.Equal(hospitalId, created.GetProperty("hospitalId").GetGuid());
        Assert.Equal(residentId, created.GetProperty("residentId").GetGuid());

        var id = created.GetProperty("id").GetGuid();
        var list = await (await _dispatcher.GetAsync(Url(incident.Id))).ExpectAsync<JsonElement>(HttpStatusCode.OK);
        Assert.Contains(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);
        await (await _dispatcher.GetAsync($"{Url(incident.Id)}/{id}")).ExpectAsync(HttpStatusCode.OK);
    }

    // TC-API-PT-02
    [Fact]
    [Trait("Requirement", "ES-FR-014")]
    public async Task Transport_HasNoMedicalHistoryFields()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
        var hospitalId = await CreateHospitalAsync();

        var created = await (await _responder.PostAsJsonAsync(Url(incident.Id), new
        {
            triageLevel = "Emergency",
            hospitalId,
            diagnosis = "should be ignored",      // extra fields must not be stored or returned
            medicalHistory = "should be ignored"
        })).ExpectAsync<JsonElement>(HttpStatusCode.Created);

        var allowed = new[] { "id", "incidentId", "triageLevel", "hospitalId", "residentId", "createdAt" };
        Assert.All(created.EnumerateObject(), p => Assert.Contains(p.Name, allowed));
        Assert.DoesNotContain("should be ignored", created.GetRawText());
    }

    // TC-API-PT-03
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task UnknownHospital_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");

        var response = await _responder.PostAsJsonAsync(Url(incident.Id),
            new { triageLevel = "Urgent", hospitalId = Guid.NewGuid() });

        Assert.Equal("invalid_transport", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-PT-04
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task MissingTriageLevel_OrEmptyResidentId_Returns400()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
        var hospitalId = await CreateHospitalAsync();

        var noTriage = await _responder.PostAsJsonAsync(Url(incident.Id), new { hospitalId });
        Assert.Equal("invalid_transport", await noTriage.ExpectErrorAsync(HttpStatusCode.BadRequest));

        var emptyResident = await _responder.PostAsJsonAsync(Url(incident.Id),
            new { triageLevel = "Urgent", hospitalId, residentId = Guid.Empty });
        Assert.Equal("invalid_transport", await emptyResident.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-PT-05
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownIncident_Returns404()
    {
        var hospitalId = await CreateHospitalAsync();

        var response = await _responder.PostAsJsonAsync(Url(Guid.NewGuid()), new { triageLevel = "Urgent", hospitalId });
        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));

        var list = await _responder.GetAsync(Url(Guid.NewGuid()));
        Assert.Equal("incident_not_found", await list.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-PT-06
    [Fact]
    [Trait("Requirement", "ES-NFR-007")]
    public async Task Viewer_CannotSeePatientTransports()
    {
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");

        Assert.Equal(HttpStatusCode.Forbidden, (await _viewer.GetAsync(Url(incident.Id))).StatusCode);
    }
}
