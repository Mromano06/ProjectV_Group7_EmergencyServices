using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// GET /health (ES-NFR-001, SH-A05): Docker, the API gateway and other teams use it to see
/// whether Emergency Services is up. It must work without logging in.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Health")]
public class HealthApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;

    public HealthApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    // TC-HLT-01
    [Fact]
    [Trait("Requirement", "ES-NFR-001")]
    public async Task Health_Returns200_WithoutLoggingIn_WhenDatabaseIsUp()
    {
        var anonymous = _factory.CreateClientAs(null);

        var response = await anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var json = await JsonOf(response);
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.Equal("emergency", json.GetProperty("service").GetString());
        Assert.Equal("Healthy", json.GetProperty("checks").GetProperty("database").GetString());
        Assert.EndsWith("Z", json.GetProperty("checkedAt").GetString()); // SH-I04 UTC
    }

    // TC-HLT-02
    [Fact]
    [Trait("Requirement", "SH-A05")]
    public async Task Health_IsAlsoReachable_ThroughTheGatewayRoute()
    {
        var anonymous = _factory.CreateClientAs(null);

        var response = await anonymous.GetAsync("/api/emergency/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", (await JsonOf(response)).GetProperty("status").GetString());
    }

    // TC-HLT-03
    [Fact]
    [Trait("Requirement", "SH-A05")]
    public async Task Health_Returns503_AndHidesErrorDetails_WhenACheckFails()
    {
        // Add a check that always fails, as if a dependency were down. Its exception holds
        // "secret" connection details that must never appear in the public response.
        using var failing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck("simulated-outage", () =>
                HealthCheckResult.Unhealthy("Simulated outage",
                    new InvalidOperationException("Server=secret-db;Password=hunter2")))));

        var response = await failing.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("Unhealthy", json.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", json.GetProperty("checks").GetProperty("simulated-outage").GetString());
        Assert.Equal("Healthy", json.GetProperty("checks").GetProperty("database").GetString());
        Assert.DoesNotContain("secret-db", body);
        Assert.DoesNotContain("hunter2", body);
    }
}
