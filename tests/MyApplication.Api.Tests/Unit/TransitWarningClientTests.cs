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

    // ---------- Road closures from our incidents (ES-FR-015, PT-FR-019) ----------

    private static readonly TransitRoadClosure Closure = new(
        ExternalId: Guid.NewGuid().ToString(),
        SourceService: TransitRoadClosure.Source,
        Title: "Fire emergency – Full closure",
        Description: "Full closure: Main St between Water and Ainslie. Fire incident, High severity. Reported by Emergency Services.",
        Type: "road_closure",
        Status: "active",
        Address: new SharedAddress("100", "Main St", "Cambridge", "N1R 1A1"),
        AffectedArea: "Main St between Water and Ainslie",
        AffectedSegments: ["Main St"],
        Latitude: 43.3601,
        Longitude: -80.3123,
        RadiusMeters: 250,
        StartTime: TransitRoadClosure.Rfc3339(new DateTime(2026, 11, 25, 22, 30, 0, DateTimeKind.Utc)),
        ValidUntil: TransitRoadClosure.UnixSeconds(new DateTime(2026, 11, 26, 1, 30, 0, DateTimeKind.Utc)));

    // TC-TRN-07
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task RoadClosure_GoesToTransitsRoadClosures_WithBearerToken_AddressAndValidUntil()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/" + TransitWarningClient.DefaultRoadClosurePath, HttpStatusCode.Created);

        var sent = await MakeClient(gateway).SendRoadClosureAsync(Closure);

        Assert.True(sent);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal("/api/transport/v1/road-closures", request.PathAndQuery);          // Transit Swagger (Nov 26)
        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", request.Authorization); // SH-S07, required
        Assert.Contains("\"address\":{\"streetNumber\":\"100\",\"streetName\":\"Main St\",\"city\":\"Cambridge\",\"postalCode\":\"N1R 1A1\"}",
            request.Body);                                                                // SH-I06, required
        Assert.Contains("\"valid-until\":1795656600", request.Body);                     // epoch seconds, required
        Assert.Contains($"\"external_id\":\"{Closure.ExternalId}\"", request.Body);   // idempotent upsert
        Assert.Contains("\"source_service\":\"emergency\"", request.Body);
        Assert.Contains("\"affected_segments\":[\"Main St\"]", request.Body);
        Assert.Contains("\"latitude\":43.3601", request.Body);
        Assert.Contains("\"radius_meters\":250", request.Body);
        Assert.DoesNotContain("end_time", request.Body);                                 // Transit: valid-until OR end_time, not both
        Assert.DoesNotContain("externalId", request.Body);                               // no camelCase names
        Assert.DoesNotContain("external_reference", request.Body);                       // Transit 400s if it differs from external_id
        Assert.DoesNotContain("valid_until", request.Body);                              // hyphen, not underscore
    }

    // TC-TRN-08
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)] // token missing, expired or not accepted
    [InlineData(HttpStatusCode.Forbidden)]
    [Trait("Requirement", "SH-A07")]
    public async Task RoadClosure_TransitRefuses_ReturnsFalse(HttpStatusCode status)
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/" + TransitWarningClient.DefaultRoadClosurePath, status, new { error = "no" });

        Assert.False(await MakeClient(gateway).SendRoadClosureAsync(Closure));
    }

    // TC-TRN-09
    [Fact]
    [Trait("Requirement", "ES-FR-015")]
    public async Task ResolvingAClosure_SendsOnlyExternalIdTitleAndStatus()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/" + TransitWarningClient.DefaultRoadClosurePath, HttpStatusCode.OK);

        var sent = await MakeClient(gateway).ResolveRoadClosureAsync(
            new TransitRoadClosureResolution(Closure.ExternalId, "Fire emergency – road reopened", "resolved"));

        Assert.True(sent);
        var request = Assert.Single(gateway.Requests);
        Assert.Equal("/api/transport/v1/road-closures", request.PathAndQuery);          // same endpoint as create
        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", request.Authorization);
        Assert.NotNull(request.Body);
        using var body = System.Text.Json.JsonDocument.Parse(request.Body);
        Assert.Equal(new[] { "external_id", "title", "status" },                         // Transit 400s on unknown fields
            body.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal(Closure.ExternalId, body.RootElement.GetProperty("external_id").GetString());
        Assert.Equal("resolved", body.RootElement.GetProperty("status").GetString());
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