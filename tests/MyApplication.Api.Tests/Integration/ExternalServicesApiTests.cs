using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// Endpoints that call OTHER teams' services through the gateway: Budget (SH-C08/09),
/// HR (SH-C10/11) and road closures from Public Transport (SH-C17, ES-FR-010).
/// The other services are faked with FakeGateway, so each test decides what they "reply".
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "External services")]
public class ExternalServicesApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;
    private readonly HttpClient _admin;
    private readonly HttpClient _dispatcher;
    private readonly HttpClient _viewer;

    public ExternalServicesApiTests(EmergencyApiFactory factory)
    {
        _factory = factory;
        _admin = factory.CreateClientAs(Roles.Admin);
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
        _viewer = factory.CreateClientAs(Roles.Viewer);
    }

    private FakeGateway Gateway => _factory.Gateway;

    // Public Transport serves everyone's road closures (SH-C17), confirmed Nov 25.
    private const string RoadClosuresPath = "/api/transport/v1/road-closures";

    // Matches Budget's expense contract (BF-03 / SH-C08): departmentCode, amount, description.
    private static object Expense(decimal amount = 250.75m) => new
    {
        departmentCode = "EMS",
        amount,
        description = "Fuel - integration test expense"
    };

    // ---------- Budget ----------

    // TC-API-EXT-01
    [Fact]
    [Trait("Requirement", "SH-C08")]
    public async Task Budget_ShowsTheAllocationFromTheBudgetService_UsingTheServiceAccount()
    {
        Gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/EMS", HttpStatusCode.OK,
            new { allocated = 500000, spent = 125000, currency = "CAD" });

        var json = await (await _viewer.GetAsync("/api/emergency/v1/budget")).ExpectAsync<JsonElement>(HttpStatusCode.OK);

        Assert.Equal(500000, json.GetProperty("allocated").GetInt32());
        var sent = Gateway.Requests.Last(r => r.PathAndQuery == "/api/budget/v1/allocations/EMS");
        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", sent.Authorization); // SH-S07
    }

    // TC-API-EXT-02
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task Budget_WhenBudgetServiceFails_Returns502_NotACrash()
    {
        Gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/EMS", HttpStatusCode.InternalServerError);

        var response = await _viewer.GetAsync("/api/emergency/v1/budget");

        Assert.Equal("budget_unavailable", await response.ExpectErrorAsync(HttpStatusCode.BadGateway));
    }

    // TC-API-EXT-03
    [Fact]
    [Trait("Requirement", "SH-C09")]
    public async Task Admin_SubmitsExpense_ItIsForwardedToBudget()
    {
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.Created, new { id = Guid.NewGuid() });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(250.75m));

        await response.ExpectAsync(HttpStatusCode.OK);
        var sent = Gateway.Requests.Last(r => r.PathAndQuery == "/api/budget/v1/expenses");
        Assert.Contains("250.75", sent.Body);
        Assert.Contains("\"departmentCode\":\"EMS\"", sent.Body);
        Assert.Contains("Fuel - integration test expense", sent.Body);
    }

    // TC-API-EXT-13
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Expense_WithoutDescription_Returns400_AndIsNotSent()
    {
        var sentBefore = Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses");

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses",
            new { departmentCode = "EMS", amount = 50m });

        Assert.Equal("validation_failed", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal(sentBefore, Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses"));
    }

    // TC-API-EXT-04
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Expense_OfZeroDollars_Returns400_AndIsNotSent()
    {
        var sentBefore = Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses");

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(sentBefore, Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses"));
    }

    // TC-API-EXT-05
    [Fact]
    [Trait("Requirement", "SH-C09")]
    public async Task Viewer_CannotSubmitExpenses_Gets403()
    {
        var response = await _viewer.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Staff / HR ----------

    // TC-API-EXT-06
    [Fact]
    [Trait("Requirement", "SH-C11")]
    public async Task StaffSummary_CountsHrStaffByProfession()
    {
        Gateway.Respond(HttpMethod.Get, "/api/hr/v1/staff?department=emergency", HttpStatusCode.OK, new[]
        {
            new { name = "A", profession = "Paramedic" },
            new { name = "B", profession = "Firefighter" },
            new { name = "C", profession = "paramedic" }
        });

        var counts = await (await _dispatcher.GetAsync("/api/emergency/v1/staff/summary"))
            .ExpectAsync<List<ProfessionCount>>(HttpStatusCode.OK);

        Assert.Equal(1, counts.Single(c => c.Profession == "Firefighter").Count);
        Assert.Equal(2, counts.Single(c => c.Profession.Equals("Paramedic", StringComparison.OrdinalIgnoreCase)).Count);
    }

    // TC-API-EXT-07
    [Fact]
    [Trait("Requirement", "SH-C10")]
    public async Task HireRequest_IsForwardedToHr()
    {
        Gateway.Respond(HttpMethod.Post, "/api/hr/v1/staff-requests", HttpStatusCode.Accepted, new { requestId = Guid.NewGuid() });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/staff/requests", new
        {
            action = "Hire", profession = "Paramedic", reason = "Winter coverage"
        });

        await response.ExpectAsync(HttpStatusCode.OK);
        Assert.Contains(Gateway.Requests, r => r.PathAndQuery == "/api/hr/v1/staff-requests" && r.Body!.Contains("Paramedic"));
    }

    // TC-API-EXT-08
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task StaffRequest_Validation_Returns400()
    {
        var badAction = await _admin.PostAsJsonAsync("/api/emergency/v1/staff/requests", new
        {
            action = "Promote", profession = "Paramedic"
        });
        var terminateWithoutId = await _admin.PostAsJsonAsync("/api/emergency/v1/staff/requests", new
        {
            action = "Terminate", profession = "Paramedic"
        });

        Assert.Equal("invalid_action", await badAction.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal("missing_staff_id", await terminateWithoutId.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-EXT-09
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task StaffSummary_WhenHrIsDown_Returns502()
    {
        Gateway.Respond(HttpMethod.Get, "/api/hr/v1/staff?department=emergency", HttpStatusCode.ServiceUnavailable);

        var response = await _dispatcher.GetAsync("/api/emergency/v1/staff/summary");

        Assert.Equal("hr_unavailable", await response.ExpectErrorAsync(HttpStatusCode.BadGateway));
    }

    // ---------- Road closures near an incident (ES-FR-010) ----------

    // TC-API-EXT-10
    [Fact]
    [Trait("Requirement", "ES-FR-010")]
    public async Task RoadClosures_OnlyThoseWithin1Km_AreShown_WithTheirEndTime()
    {
        var incident = await _dispatcher.CreateIncidentAsync();
        var endsAt = DateTimeOffset.UtcNow.AddHours(4);
        Gateway.Respond(HttpMethod.Get, RoadClosuresPath, HttpStatusCode.OK, new[]
        {
            new { id = "near", description = "Water main repair", latitude = TestData.CityLat + 0.0045, longitude = TestData.CityLon, expectedEndAt = endsAt.ToString("O") },
            new { id = "far", description = "Bridge work", latitude = TestData.CityLat + 0.045, longitude = TestData.CityLon, expectedEndAt = endsAt.ToString("O") }
        });

        var closures = await (await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{incident.Id}/road-closures"))
            .ExpectAsync<List<NearbyRoadClosureDto>>(HttpStatusCode.OK);

        var near = Assert.Single(closures);
        Assert.Equal("near", near.Id);
        Assert.True(near.DistanceKm < 1.0);
        Assert.Equal("distance", near.MatchedBy);
        Assert.NotNull(near.ExpectedEndAt);
    }

    // TC-API-EXT-11
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task RoadClosures_WhenTransitIsDown_ReturnsAClearError()
    {
        var incident = await _dispatcher.CreateIncidentAsync();
        Gateway.Respond(HttpMethod.Get, RoadClosuresPath, HttpStatusCode.InternalServerError);

        var response = await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{incident.Id}/road-closures");

        Assert.Equal("road_closures_unavailable", await response.ExpectErrorAsync(HttpStatusCode.BadGateway));
    }

    // TC-API-EXT-12
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task RoadClosures_ForUnknownIncident_Returns404()
    {
        var response = await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{Guid.NewGuid()}/road-closures");

        Assert.Equal("incident_not_found", await response.ExpectErrorAsync(HttpStatusCode.NotFound));
    }

    // TC-API-EXT-14
    [Fact]
    [Trait("Requirement", "ES-FR-010")]
    public async Task RoadClosures_InTransitsShape_AreShown_WhenTheyNameTheIncidentsStreet()
    {
        // Transit's Swagger (Nov 25): DisruptionResponse[], snake_case, no coordinates.
        var incident = await _dispatcher.CreateIncidentAsync(); // "100 King St W"
        var endsAt = DateTimeOffset.UtcNow.AddHours(2);
        Gateway.Respond(HttpMethod.Get, RoadClosuresPath, HttpStatusCode.OK, new object[]
        {
            new { id = 11, title = "Water main repair", affected_area = "King Street West between Ainslie and Water",
                  affected_segments = Array.Empty<string>(), status = "active", type = "road_closure",
                  end_time = endsAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
            new { id = 12, title = "Paving", affected_area = "Downtown", affected_segments = new[] { "King St" },
                  status = "active", type = "road_closure" },                          // plain "King St" matches too
            new { id = 13, title = "Bridge work", affected_area = "Main St bridge", affected_segments = new[] { "Main St" },
                  status = "active", type = "road_closure" }                           // another street: left out
        });

        var closures = await (await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{incident.Id}/road-closures"))
            .ExpectAsync<List<NearbyRoadClosureDto>>(HttpStatusCode.OK);

        Assert.Equal(new[] { "11", "12" }, closures.Select(c => c.Id));
        Assert.All(closures, c => Assert.Equal("street", c.MatchedBy));
        Assert.Equal("Water main repair – King Street West between Ainslie and Water", closures[0].Description);
        Assert.Equal(endsAt.ToUnixTimeSeconds(), closures[0].ExpectedEndAt!.Value.ToUnixTimeSeconds()); // expected end time
        Assert.Null(closures[0].DistanceKm);
    }

    // TC-API-EXT-22
    [Fact]
    [Trait("Requirement", "ES-FR-010")]
    public async Task RoadClosures_ThatHaveEnded_AreLeftOut()
    {
        var incident = await _dispatcher.CreateIncidentAsync(); // "100 King St W"
        Gateway.Respond(HttpMethod.Get, RoadClosuresPath, HttpStatusCode.OK, new object[]
        {
            new { id = 21, title = "Done", affected_area = "King St W", status = "resolved" },
            new { id = 22, title = "Past", affected_area = "King St W", status = "active",
                  end_time = DateTimeOffset.UtcNow.AddHours(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") },
            new { id = 23, title = "Still closed", affected_area = "King St W", status = "active" }
        });

        var closures = await (await _dispatcher.GetAsync($"/api/emergency/v1/incidents/{incident.Id}/road-closures"))
            .ExpectAsync<List<NearbyRoadClosureDto>>(HttpStatusCode.OK);

        Assert.Equal("23", Assert.Single(closures).Id);
    }

    // TC-API-EXT-15
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task Expense_IsAlwaysSentForEms_EvenIfTheCallerNamesAnotherDepartment()
    {
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.Created, new { id = Guid.NewGuid() });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses",
            new { departmentCode = "HR", amount = 75m, description = "Fuel" });

        await response.ExpectAsync(HttpStatusCode.OK);
        var sent = Gateway.Requests.Last(r => r.PathAndQuery == "/api/budget/v1/expenses");
        Assert.Contains("\"departmentCode\":\"EMS\"", sent.Body);
        Assert.DoesNotContain("\"HR\"", sent.Body);
    }

    // TC-API-EXT-16
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task Expense_OverBudget_Returns400_WithBudgetsReason()
    {
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.BadRequest,
            new { error = "over_budget", message = "Expense exceeds the remaining EMS budget.", traceId = "t" });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(999999m));

        Assert.Equal("budget_rejected", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Contains("exceeds the remaining EMS budget", await response.Content.ReadAsStringAsync());
    }

    // TC-API-EXT-17
    [Fact]
    [Trait("Requirement", "ES-NFR-004")]
    public async Task Expense_WithMoreThanTwoDecimals_Returns400_AndIsNotSent()
    {
        var sentBefore = Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses");

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(10.005m));

        Assert.Equal("invalid_amount", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Equal(sentBefore, Gateway.Requests.Count(r => r.PathAndQuery == "/api/budget/v1/expenses"));
    }

    // TC-API-EXT-18
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task FundingRequest_IsForwardedToBudget_AsUrgentEms()
    {
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/funding-requests", HttpStatusCode.Created,
            new { id = Guid.NewGuid(), status = "Pending" });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/funding-requests",
            new { amount = 50000m, reason = "Winter storm overtime", urgent = true });

        var json = await response.ExpectAsync<JsonElement>(HttpStatusCode.OK);
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        var sent = Gateway.Requests.Last(r => r.PathAndQuery == "/api/budget/v1/funding-requests");
        Assert.Contains("\"departmentCode\":\"EMS\"", sent.Body);
        Assert.Contains("\"urgent\":true", sent.Body);
        Assert.Contains("Winter storm overtime", sent.Body);
        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", sent.Authorization); // SH-S07
    }

    // TC-API-EXT-19
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task FundingRequest_OnlyAdmin_CanAsk()
    {
        var response = await _dispatcher.PostAsJsonAsync("/api/emergency/v1/budget/funding-requests",
            new { amount = 100m, reason = "x", urgent = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // TC-API-EXT-20
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task Expense_ThatBudgetAnswersWithApprovedFalse_Returns400_WithBudgetsMessage()
    {
        // Budget's ExpenseResult: { approved, message, remainingBudget, expense } (live Swagger, Nov 25).
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.OK,
            new { approved = false, message = "Over the remaining EMS budget.", remainingBudget = 10m });

        var response = await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(500m));

        Assert.Equal("budget_rejected", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
        Assert.Contains("Over the remaining EMS budget.", await response.Content.ReadAsStringAsync());
    }

    // TC-API-EXT-21
    [Fact]
    [Trait("Requirement", "ES-FR-025")]
    public async Task ApprovedExpense_ReturnsBudgetsResult()
    {
        Gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.Created,
            new { approved = true, message = "Expense Approved", remainingBudget = 4499000m });

        var json = await (await _admin.PostAsJsonAsync("/api/emergency/v1/budget/expenses", Expense(1000m)))
            .ExpectAsync<JsonElement>(HttpStatusCode.OK);

        Assert.True(json.GetProperty("approved").GetBoolean());
        Assert.Equal(4499000m, json.GetProperty("remainingBudget").GetDecimal());
    }
}
