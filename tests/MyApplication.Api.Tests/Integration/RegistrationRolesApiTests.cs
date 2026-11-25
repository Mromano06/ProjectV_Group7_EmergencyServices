using System.Net;
using System.Net.Http.Json;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Logging in with Registration's role names (Manager, Dispatcher, User) gives the right
/// Emergency access through the real API (RoleMapping in appsettings.json).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Login and roles")]
public class RegistrationRolesApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;

    public RegistrationRolesApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
    }

    // TC-API-ROLE-01
    [Fact]
    [Trait("Requirement", "ES-FR-001")]
    public async Task RegistrationDispatcher_CanCreateIncidents()
    {
        var dispatcher = _factory.CreateClientAs("Dispatcher");

        var response = await dispatcher.PostAsJsonAsync("/api/emergency/v1/incidents", TestData.NewIncident());

        await response.ExpectAsync(HttpStatusCode.Created);
    }

    // TC-API-ROLE-02
    [Fact]
    [Trait("Requirement", "ES-FR-011")]
    public async Task RegistrationManager_IsAnEmergencyAdmin()
    {
        var manager = _factory.CreateClientAs("Manager");

        var station = await manager.CreateStationAsync(); // Admin-only

        Assert.NotEqual(Guid.Empty, station.Id);
    }

    // TC-API-ROLE-03
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task RegistrationUser_IsReadOnly()
    {
        var user = _factory.CreateClientAs("User");

        Assert.Equal(HttpStatusCode.OK,
            (await user.GetAsync("/api/emergency/v1/weather-warnings?active=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/emergency/v1/incidents", TestData.NewIncident())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await user.GetAsync("/api/emergency/v1/incidents")).StatusCode); // no caller details for Viewers
    }

    // TC-API-ROLE-04
    [Theory]
    [Trait("Requirement", "SH-S03")]
    [InlineData("Police Officer")]
    [InlineData("Administrator")]
    public async Task OtherRegistrationRoles_GetNoEmergencyAccess(string role)
    {
        var other = _factory.CreateClientAs(role);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PostAsJsonAsync("/api/emergency/v1/incidents", TestData.NewIncident())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync("/api/emergency/v1/units")).StatusCode);
    }
}
