using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Dashboard numbers and CSV export (ES-FR-024, ES-FR-027, ES-NFR-007).
/// Tests compare "before" and "after" numbers, so incidents made by other tests don't matter.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Stats and export")]
public class StatsAndExportApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _responder;
    private readonly HttpClient _viewer;

    // A window around "now" – every incident these tests create falls inside it.
    private readonly string _from = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
    private readonly string _to = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));

    public StatsAndExportApiTests(EmergencyApiFactory factory)
    {
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _responder = factory.CreateClientAs(Roles.Responder);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    private async Task<StatsDto> GetStatsAsync(HttpClient client) =>
        await (await client.GetAsync($"/api/emergency/v1/stats?from={_from}&to={_to}"))
            .ExpectAsync<StatsDto>(HttpStatusCode.OK);

    // ---------- Stats ----------

    // TC-API-STA-01
    [Fact]
    [Trait("Requirement", "ES-FR-024")]
    public async Task Stats_CountTheNewIncident_ItsDeaths_AndResponseTime()
    {
        var before = await GetStatsAsync(_viewer);

        var station = await _admin.CreateStationAsync();
        var unit = await _admin.CreateUnitAsync(station.Id, "Ambulance");
        var incident = await _dispatcher.CreateIncidentAsync(type: "Medical");
        await _dispatcher.ResolveAsync(incident.Id, unit.Id);
        await (await _responder.FileReportAsync(incident.Id, deaths: 2)).ExpectAsync(HttpStatusCode.OK);

        var after = await GetStatsAsync(_viewer);

        Assert.Equal(before.TotalIncidents + 1, after.TotalIncidents);
        Assert.Equal(before.ByType.GetValueOrDefault("Medical") + 1, after.ByType["Medical"]);
        Assert.Equal(before.ByStatus.GetValueOrDefault("Closed") + 1, after.ByStatus["Closed"]);
        Assert.Equal(before.DeathCount + 2, after.DeathCount);
        Assert.NotNull(after.AverageResponseMinutes);
        Assert.True(after.AverageResponseMinutes >= 0);
    }

    // TC-API-STA-02
    [Fact]
    [Trait("Requirement", "ES-FR-024")]
    public async Task Stats_WithoutADateRange_Returns400()
    {
        var response = await _viewer.GetAsync("/api/emergency/v1/stats");

        Assert.Equal("date_range_required", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-STA-03
    [Fact]
    [Trait("Requirement", "ES-FR-024")]
    public async Task Stats_WithFromAfterTo_Returns400()
    {
        var response = await _viewer.GetAsync($"/api/emergency/v1/stats?from={_to}&to={_from}");

        Assert.Equal("invalid_date_range", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // ---------- CSV export ----------

    // TC-API-EXP-01
    [Fact]
    [Trait("Requirement", "ES-FR-027")]
    public async Task Export_IsACsvWithOneRowPerIncidentInRange()
    {
        var incident = await _dispatcher.CreateIncidentAsync();

        var response = await _dispatcher.GetAsync($"/api/emergency/v1/incidents/export?from={_from}&to={_to}");
        await response.ExpectAsync(HttpStatusCode.OK);
        var csv = await response.Content.ReadAsStringAsync();
        var stats = await GetStatsAsync(_dispatcher);

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Id,Type,Severity,Status,ReportedAt", lines[0]);
        Assert.Contains(lines, l => l.StartsWith(incident.Id.ToString()));
        Assert.Equal(stats.TotalIncidents, lines.Length - 1); // header + one row per incident
    }

    // TC-API-EXP-02
    [Fact]
    [Trait("Requirement", "ES-NFR-007")]
    public async Task Export_NeverContainsCallerDetails()
    {
        await _dispatcher.CreateIncidentAsync(callerName: "Secret Caller Name");

        var csv = await _dispatcher.GetStringAsync($"/api/emergency/v1/incidents/export?from={_from}&to={_to}");

        Assert.DoesNotContain("Secret Caller Name", csv);
        Assert.DoesNotContain("519-555-0100", csv);
    }

    // TC-API-EXP-03
    [Theory]
    [InlineData(Roles.Responder)]
    [InlineData(Roles.Viewer)]
    [Trait("Requirement", "ES-FR-027")]
    public async Task Export_IsOnlyForAdminAndDispatcher(string role)
    {
        var client = role == Roles.Responder ? _responder : _viewer;

        var response = await client.GetAsync($"/api/emergency/v1/incidents/export?from={_from}&to={_to}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-EXP-04
    [Fact]
    [Trait("Requirement", "ES-FR-027")]
    public async Task Export_WithoutADateRange_Returns400()
    {
        var response = await _admin.GetAsync("/api/emergency/v1/incidents/export");

        Assert.Equal("date_range_required", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }
}
