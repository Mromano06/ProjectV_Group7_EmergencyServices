using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyApplication.Api.Clients;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests;

/// <summary>
/// Tests for TransitWarningClient: the notice goes through the gateway with our
/// service-account token, and every failure returns false instead of crashing.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Weather warnings")]
public class TransitWarningClientTests
{
    private const string GatewayPath = "/" + TransitWarningClient.DefaultPath;

    private static readonly TransitWarningNotice Notice = new(
        Guid.NewGuid(), "Freezing Rain", "Warning", "City-wide",
        DateTime.UtcNow, DateTime.UtcNow.AddHours(6), "Active");

    private static TransitWarningClient MakeClient(
        HttpMessageHandler handler,
        string? gatewayUrl = FakeGateway.BaseUrl,
        IServiceTokenProvider? tokens = null,
        string? configuredPath = null)
    {
        var http = new HttpClient(handler);
        if (gatewayUrl != null)
        {
            http.BaseAddress = new Uri(gatewayUrl);
        }

        var settings = new Dictionary<string, string?>();
        if (configuredPath != null)
        {
            settings["Services:TransitWarningPath"] = configuredPath;
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new TransitWarningClient(http, tokens ?? new FakeServiceTokenProvider(), config,
            NullLogger<TransitWarningClient>.Instance);
    }

    // TC-TRN-01
    [Fact]
    [Trait("Requirement", "SH-A03")]
    public async Task Notice_GoesThroughTheGateway_WithTheServiceToken_AndTheWarningDetails()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, GatewayPath, HttpStatusCode.Accepted);

        var sent = await MakeClient(gateway).SendWarningNoticeAsync(Notice);

        Assert.True(sent);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(GatewayPath, request.PathAndQuery);
        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", request.Authorization); // SH-S07
        Assert.Contains(Notice.WarningId.ToString(), request.Body);
        Assert.Contains("\"severity\":\"Warning\"", request.Body);                       // camelCase JSON
    }

    // TC-TRN-02
    [Fact]
    [Trait("Requirement", "SH-A06")]
    public async Task ConfiguredPath_IsUsedInsteadOfTheDefault()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/api/transit/v1/alerts", HttpStatusCode.OK);

        var sent = await MakeClient(gateway, configuredPath: "api/transit/v1/alerts").SendWarningNoticeAsync(Notice);

        Assert.True(sent);
        Assert.Equal("/api/transit/v1/alerts", Assert.Single(gateway.Requests).PathAndQuery);
    }

    // TC-TRN-03
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task TransitError_ReturnsFalse()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, GatewayPath, HttpStatusCode.InternalServerError);

        Assert.False(await MakeClient(gateway).SendWarningNoticeAsync(Notice));
    }

    // TC-TRN-04
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task NoGatewayConfigured_ReturnsFalse_WithoutSendingAnything()
    {
        var gateway = new FakeGateway();

        Assert.False(await MakeClient(gateway, gatewayUrl: null).SendWarningNoticeAsync(Notice));
        Assert.Empty(gateway.Requests);
    }

    // TC-TRN-05
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task NoServiceToken_StillSends_WithoutAnAuthorizationHeader()
    {
        // Registration has no service accounts yet, so notices go out without a token rather than not at all.
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, GatewayPath, HttpStatusCode.Accepted);

        var sent = await MakeClient(gateway, tokens: new MissingTokenProvider()).SendWarningNoticeAsync(Notice);

        Assert.True(sent);
        Assert.Null(Assert.Single(gateway.Requests).Authorization);
    }

    // TC-TRN-06
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task GatewayUnreachable_ReturnsFalse_InsteadOfThrowing()
    {
        Assert.False(await MakeClient(new UnreachableHandler()).SendWarningNoticeAsync(Notice));
    }

    // ---------- Road-restricting incidents (ES-FR-015, PT-FR-019) ----------

    private static readonly TransitEmergencyIncident Incident = new(
        ExternalId: Guid.NewGuid().ToString(),
        ExternalReference: "EMS-1234ABCD",
        Title: "Fire emergency – Full closure",
        Description: "Full closure: Main St between Water and Ainslie. Reported by Emergency Services.",
        Type: "road_closure",
        IncidentType: "fire",
        Severity: "high",
        Status: "active",
        AffectedArea: "Main St between Water and Ainslie",
        AffectedSegments: ["Main St"],
        Latitude: 43.3601,
        Longitude: -80.3123,
        RadiusMeters: 250,
        StartTime: TransitEmergencyIncident.Rfc3339(new DateTime(2026, 11, 25, 22, 30, 0, DateTimeKind.Utc)),
        EndTime: TransitEmergencyIncident.Rfc3339(new DateTime(2026, 11, 26, 1, 30, 0, DateTimeKind.Utc)));

    // TC-TRN-07
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task Incident_GoesToTransitsEmergencyIntegration_InTheirSnakeCaseShape()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/" + TransitWarningClient.DefaultEmergencyIncidentPath, HttpStatusCode.Created);

        var sent = await MakeClient(gateway).SendEmergencyIncidentAsync(Incident);

        Assert.True(sent);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal("/api/transport/v1/integrations/emergency/incidents", request.PathAndQuery); // Transit Swagger, PT-FR-019
        Assert.Contains($"\"external_id\":\"{Incident.ExternalId}\"", request.Body);
        Assert.Contains("\"affected_area\":\"Main St between Water and Ainslie\"", request.Body);
        Assert.Contains("\"affected_segments\":[\"Main St\"]", request.Body);
        Assert.Contains("\"latitude\":43.3601", request.Body);
        Assert.Contains("\"radius_meters\":250", request.Body);
        Assert.Contains("\"start_time\":\"2026-11-25T22:30:00Z\"", request.Body);   // RFC 3339, UTC (SH-I04)
        Assert.Contains("\"end_time\":\"2026-11-26T01:30:00Z\"", request.Body);
        Assert.DoesNotContain("externalId", request.Body);                         // no camelCase names
    }

    // TC-TRN-08
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)] // Transit's write endpoints want an admin browser session for now
    [InlineData(HttpStatusCode.Forbidden)]
    [Trait("Requirement", "SH-A07")]
    public async Task Incident_TransitRefuses_ReturnsFalse(HttpStatusCode status)
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/" + TransitWarningClient.DefaultEmergencyIncidentPath, status, new { error = "no" });

        Assert.False(await MakeClient(gateway).SendEmergencyIncidentAsync(Incident));
    }

    private sealed class MissingTokenProvider : IServiceTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken ct) =>
            throw new ExternalServiceException("Service-account token is not configured.");
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("No such host is known.");
    }
}
