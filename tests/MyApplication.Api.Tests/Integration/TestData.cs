using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Dtos;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Shortcuts for setting up test data through the real API, e.g.
///     var station = await _admin.CreateStationAsync();
///     var unit    = await _admin.CreateUnitAsync(station.Id);
///     var incident = await _dispatcher.CreateIncidentAsync();
/// Each one fails the test with the API's own error message if the call doesn't work.
/// </summary>
public static class TestData
{
    // Somewhere in Kitchener-Waterloo. 0.0045 degrees of latitude is about 0.5 km.
    public const double CityLat = 43.4500;
    public const double CityLon = -80.4900;

    public static string UniqueCallSign() => "T-" + Guid.NewGuid().ToString("N")[..10];

    public static async Task<StationDto> CreateStationAsync(this HttpClient admin,
        double lat = CityLat, double lon = CityLon, string type = "Fire", string? name = null)
    {
        var response = await admin.PostAsJsonAsync("/api/emergency/v1/stations", new
        {
            name = name ?? "Station " + Guid.NewGuid().ToString("N")[..6],
            type,
            latitude = lat,
            longitude = lon,
            address = "1 Test St"
        });
        return await response.ExpectAsync<StationDto>(HttpStatusCode.Created);
    }

    public static async Task<UnitDto> CreateUnitAsync(this HttpClient admin, Guid stationId, string type = "Engine")
    {
        var response = await admin.PostAsJsonAsync("/api/emergency/v1/units", new
        {
            callSign = UniqueCallSign(),
            type,
            stationId
        });
        return await response.ExpectAsync<UnitDto>(HttpStatusCode.Created);
    }

    public static async Task<IncidentDetailDto> CreateIncidentAsync(this HttpClient dispatcher,
        double lat = CityLat, double lon = CityLon, string type = "Fire", string severity = "High",
        Guid? sourceCallId = null, string? callerName = "Pat Caller")
    {
        var response = await dispatcher.PostAsJsonAsync("/api/emergency/v1/incidents",
            NewIncident(lat, lon, type, severity, sourceCallId, callerName));
        return await response.ExpectAsync<IncidentDetailDto>(HttpStatusCode.Created);
    }

    /// <summary>A valid create-incident body; tests change one field to break it.</summary>
    public static object NewIncident(double lat = CityLat, double lon = CityLon, string type = "Fire",
        string severity = "High", Guid? sourceCallId = null, string? callerName = "Pat Caller") => new
    {
        type,
        severity,
        location = new { latitude = lat, longitude = lon, address = "100 King St W" },
        description = "Created by an integration test",
        callerName,
        callerPhone = "519-555-0100",
        sourceCallId
    };

    public static Task<HttpResponseMessage> ChangeStatusAsync(this HttpClient client, Guid incidentId, string status) =>
        client.PatchAsJsonAsync($"/api/emergency/v1/incidents/{incidentId}/status", new { status });

    public static Task<HttpResponseMessage> AssignAsync(this HttpClient client, Guid incidentId, params Guid[] unitIds) =>
        client.PostAsJsonAsync($"/api/emergency/v1/incidents/{incidentId}/assignments", new { unitIds });

    public static Task<HttpResponseMessage> FileReportAsync(this HttpClient client, Guid incidentId, int deaths = 0, int injuries = 0) =>
        client.PostAsJsonAsync($"/api/emergency/v1/incidents/{incidentId}/report", new
        {
            outcome = "Fire extinguished",
            injuries,
            deaths,
            hydrantWaterLitres = 1200.5,
            notes = "Integration test report"
        });

    /// <summary>Takes an incident from Reported all the way to Resolved, with one unit.</summary>
    public static async Task ResolveAsync(this HttpClient dispatcher, Guid incidentId, Guid unitId)
    {
        await (await dispatcher.AssignAsync(incidentId, unitId)).ExpectAsync(HttpStatusCode.Created);
        foreach (var status in new[] { "EnRoute", "OnScene", "Resolved" })
        {
            await (await dispatcher.ChangeStatusAsync(incidentId, status)).ExpectAsync(HttpStatusCode.OK);
        }
    }

    // ---------- Checking responses ----------

    /// <summary>Checks the status code (showing the response body if it's wrong) and reads the JSON.</summary>
    public static async Task<T> ExpectAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        await response.ExpectAsync(expected);
        var value = await response.Content.ReadFromJsonAsync<T>();
        Assert.NotNull(value);
        return value!;
    }

    /// <summary>Checks the status code, showing the response body in the failure message if it's wrong.</summary>
    public static async Task ExpectAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} from {response.RequestMessage?.Method} " +
                        $"{response.RequestMessage?.RequestUri?.PathAndQuery} but got {(int)response.StatusCode} " +
                        $"{response.StatusCode}: {body}");
        }
    }

    /// <summary>Checks the status code and that the body is the shared error shape (SH-I03); returns the error code.</summary>
    public static async Task<string> ExpectErrorAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var error = await response.ExpectAsync<ErrorResponse>(expected);
        Assert.False(string.IsNullOrWhiteSpace(error.Error), "Error body has no 'error' code");
        Assert.False(string.IsNullOrWhiteSpace(error.Message), "Error body has no 'message'");
        Assert.False(string.IsNullOrWhiteSpace(error.TraceId), "Error body has no 'traceId'");
        return error.Error;
    }
}
