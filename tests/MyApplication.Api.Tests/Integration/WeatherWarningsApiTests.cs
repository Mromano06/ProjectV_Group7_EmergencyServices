using System.Net;
using System.Net.Http.Json;
using MyApplication.Api.Models;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// API tests for Weather &amp; Public Warnings (ES-FR-021, ES-FR-022, SH-C13 + board stories).
/// Each test calls the real endpoints over HTTP, as a real user with a real role would.
///
/// Tests in this class share one in-memory database, so each test creates its own warnings
/// and only checks those – never "there are exactly N warnings".
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Weather warnings")]
public class WeatherWarningsApiTests : IClassFixture<EmergencyApiFactory>
{
    private const string Url = "/api/emergency/v1/weather-warnings";

    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _viewer;
    private readonly HttpClient _anonymous;

    public WeatherWarningsApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _viewer = factory.CreateClientAs(Roles.Viewer);
        _anonymous = factory.CreateClientAs(role: null);
    }

    // ---------- Helpers ----------

    private static CreateWeatherWarningRequest NewWarning(bool affectsTransit = false) => new()
    {
        Type = "Snowfall",
        Severity = "Warning",
        Area = "City-wide",
        Description = "Created by an integration test",
        EndsAt = DateTime.UtcNow.AddHours(6),
        AffectsTransit = affectsTransit
    };

    private static UpdateWeatherWarningRequest Extend(DateTime endsAt) => new()
    {
        Type = "Snowfall",
        Severity = "Warning",
        Area = "City-wide",
        EndsAt = endsAt
    };

    /// <summary>Issues a warning as Admin and returns it (fails the test if that doesn't work).</summary>
    private async Task<WeatherWarningDto> CreateWarningAsync(bool affectsTransit = false)
    {
        var response = await _admin.PostAsJsonAsync(Url, NewWarning(affectsTransit));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WeatherWarningDto>())!;
    }

    private static async Task<ErrorResponse> ReadErrorAsync(HttpResponseMessage response)
    {
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error!.TraceId)); // SH-I03 shape
        return error;
    }

    // ---------- Issue a warning ----------

    // TC-API-WW-01
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task Admin_CanIssueAWarning_AndGets201WithLocation()
    {
        var response = await _admin.PostAsJsonAsync(Url, NewWarning());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var warning = await response.Content.ReadFromJsonAsync<WeatherWarningDto>();
        Assert.NotNull(warning);
        Assert.Equal("Active", warning!.Status);
        Assert.Equal("Manual", warning.Source);
        Assert.Equal(TestAuthHandler.UserId, warning.CreatedBy);
        Assert.Contains(warning.Id.ToString(), response.Headers.Location?.ToString());
    }

    // TC-API-WW-02
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task Viewer_CannotIssueAWarning_Gets403()
    {
        var response = await _viewer.PostAsJsonAsync(Url, NewWarning());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-WW-03
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task NotLoggedIn_CannotIssueAWarning_Gets401()
    {
        var response = await _anonymous.PostAsJsonAsync(Url, NewWarning());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // TC-API-WW-04
    [Fact]
    [Trait("Requirement", "SH-I03")]
    public async Task InvalidSeverity_Returns400_InTheSharedErrorShape()
    {
        var request = NewWarning();
        request.Severity = "Scary";

        var response = await _admin.PostAsJsonAsync(Url, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_severity", (await ReadErrorAsync(response)).Error);
    }

    // TC-API-WW-05
    [Fact]
    [Trait("Requirement", "SH-I03")]
    public async Task MissingRequiredField_Returns400_InTheSharedErrorShape()
    {
        // No "type" at all
        var response = await _admin.PostAsJsonAsync(Url, new
        {
            severity = "Warning",
            area = "City-wide",
            endsAt = DateTime.UtcNow.AddHours(3)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", (await ReadErrorAsync(response)).Error);
    }

    // TC-API-WW-06
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task EndBeforeStart_Returns400()
    {
        var request = NewWarning();
        request.StartsAt = DateTime.UtcNow.AddHours(5);
        request.EndsAt = DateTime.UtcNow.AddHours(1);

        var response = await _admin.PostAsJsonAsync(Url, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_time_window", (await ReadErrorAsync(response)).Error);
    }

    // ---------- Read warnings ----------

    // TC-API-WW-07
    [Fact]
    [Trait("Requirement", "ES-FR-022")]
    public async Task Viewer_CanReadActiveWarnings()
    {
        var created = await CreateWarningAsync();

        var active = await _viewer.GetFromJsonAsync<List<WeatherWarningDto>>($"{Url}?active=true");

        Assert.NotNull(active);
        Assert.Contains(active!, w => w.Id == created.Id);
        Assert.All(active!, w => Assert.Equal("Active", w.Status));
    }

    // TC-API-WW-08
    [Fact]
    [Trait("Requirement", "ES-FR-022")]
    public async Task History_IsPaged()
    {
        await CreateWarningAsync();
        await CreateWarningAsync();

        var page = await _viewer.GetFromJsonAsync<PagedResult<WeatherWarningDto>>($"{Url}?page=1&pageSize=1");

        Assert.NotNull(page);
        Assert.Single(page!.Items);
        Assert.Equal(1, page.PageSize);
        Assert.True(page.TotalCount >= 2);
    }

    // TC-API-WW-09
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task UnknownWarning_Returns404_InTheSharedErrorShape()
    {
        var response = await _viewer.GetAsync($"{Url}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("warning_not_found", (await ReadErrorAsync(response)).Error);
    }

    // ---------- Update / extend ----------

    // TC-API-WW-10
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task Admin_CanExtendAnActiveWarning()
    {
        var created = await CreateWarningAsync();
        var newEnd = DateTime.UtcNow.AddDays(2);

        var response = await _admin.PutAsJsonAsync($"{Url}/{created.Id}", Extend(newEnd));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WeatherWarningDto>();
        Assert.True(updated!.EndsAt > created.EndsAt);
        Assert.NotNull(updated.UpdatedAt);
    }

    // TC-API-WW-11
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task Dispatcher_CannotUpdateAWarning_Gets403()
    {
        var created = await CreateWarningAsync();

        var response = await _dispatcher.PutAsJsonAsync($"{Url}/{created.Id}", Extend(DateTime.UtcNow.AddDays(1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- End early / auto-expire ----------

    // TC-API-WW-12
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task EndingEarly_RemovesItFromActive_ButKeepsItInHistory()
    {
        var created = await CreateWarningAsync();

        var expire = await _admin.PostAsync($"{Url}/{created.Id}/expire", null);
        Assert.Equal(HttpStatusCode.OK, expire.StatusCode);

        var active = await _viewer.GetFromJsonAsync<List<WeatherWarningDto>>($"{Url}?active=true");
        Assert.DoesNotContain(active!, w => w.Id == created.Id);

        var stillThere = await _viewer.GetFromJsonAsync<WeatherWarningDto>($"{Url}/{created.Id}");
        Assert.Equal("Ended", stillThere!.Status);
    }

    // TC-API-WW-13
    [Fact]
    [Trait("Requirement", "ES-FR-021")]
    public async Task EndedWarning_CannotBeUpdatedOrEndedAgain_Returns409()
    {
        var created = await CreateWarningAsync();
        await _admin.PostAsync($"{Url}/{created.Id}/expire", null);

        var update = await _admin.PutAsJsonAsync($"{Url}/{created.Id}", Extend(DateTime.UtcNow.AddDays(1)));
        var expireAgain = await _admin.PostAsync($"{Url}/{created.Id}/expire", null);

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal("warning_ended", (await ReadErrorAsync(update)).Error);
        Assert.Equal(HttpStatusCode.Conflict, expireAgain.StatusCode);
    }

    // ---------- Simulator ----------

    // TC-API-WW-14
    [Fact]
    [Trait("Requirement", "ES-FR-023")]
    public async Task Simulate_CreatesASimulatedWarning()
    {
        var response = await _admin.PostAsync($"{Url}/simulate", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var warning = await response.Content.ReadFromJsonAsync<WeatherWarningDto>();
        Assert.Equal("Simulated", warning!.Source);
        Assert.StartsWith("[SIMULATED]", warning.Description);
    }

    // ---------- Link incidents (Dispatch Awareness) ----------

    // TC-API-WW-15
    [Fact]
    [Trait("Requirement", "Board: link incidents to a warning")]
    public async Task Dispatcher_CanLinkAnIncident_ButNotTwice()
    {
        var created = await CreateWarningAsync();
        var incidentId = Guid.NewGuid();

        var first = await _dispatcher.PostAsJsonAsync($"{Url}/{created.Id}/incidents", new { incidentId });
        var second = await _dispatcher.PostAsJsonAsync($"{Url}/{created.Id}/incidents", new { incidentId });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var linked = await first.Content.ReadFromJsonAsync<WeatherWarningDto>();
        Assert.Contains(incidentId, linked!.LinkedIncidentIds);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_linked", (await ReadErrorAsync(second)).Error);
    }

    // ---------- Public Transport ----------

    // TC-API-WW-16
    [Fact]
    [Trait("Requirement", "Board: notify Public Transport")]
    public async Task TransitAffectingWarning_IsSentToPublicTransport()
    {
        var created = await CreateWarningAsync(affectsTransit: true);

        Assert.Contains(_factory.Transit.Sent, n => n.WarningId == created.Id);
        Assert.NotNull(created.LastSentToTransitAt);
    }

    // TC-API-WW-17
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task NotifyTransit_Returns502_WhenPublicTransportIsDown_ButWarningIsKept()
    {
        var created = await CreateWarningAsync();
        _factory.Transit.ShouldSucceed = false;
        try
        {
            var response = await _admin.PostAsync($"{Url}/{created.Id}/notify-transit", null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Equal("transit_unavailable", (await ReadErrorAsync(response)).Error);

            var stillThere = await _viewer.GetAsync($"{Url}/{created.Id}");
            Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        }
        finally
        {
            _factory.Transit.ShouldSucceed = true; // put it back for the other tests
        }
    }
}
