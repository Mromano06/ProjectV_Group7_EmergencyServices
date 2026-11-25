using System.Net;
using System.Text.Json;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// The live board list (GET /incidents) that the dispatch console polls (ES-FR-004).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Incidents")]
public class LiveBoardApiTests : IClassFixture<EmergencyApiFactory>
{
    private readonly HttpClient _dispatcher;

    public LiveBoardApiTests(EmergencyApiFactory factory)
    {
        _dispatcher = factory.CreateClientAs(Roles.Dispatcher);
    }

    // TC-API-INC-20
    [Fact]
    [Trait("Requirement", "ES-FR-004")]
    public async Task LiveBoard_ShowsAddressAndDescription_ButNeverCallerDetails()
    {
        var created = await _dispatcher.CreateIncidentAsync(type: "Medical", callerName: "Pat Caller");

        var json = await (await _dispatcher.GetAsync("/api/emergency/v1/incidents?pageSize=100"))
            .ExpectAsync<JsonElement>(HttpStatusCode.OK);
        var row = json.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == created.Id);

        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("address").GetString()));
        Assert.True(row.TryGetProperty("description", out _));
        // The board is a list for everyone with access; caller details stay on the detail view (ES-NFR-007).
        Assert.False(row.TryGetProperty("callerName", out _));
        Assert.False(row.TryGetProperty("callerPhone", out _));
        Assert.DoesNotContain("Pat Caller", row.GetRawText());
    }
}
