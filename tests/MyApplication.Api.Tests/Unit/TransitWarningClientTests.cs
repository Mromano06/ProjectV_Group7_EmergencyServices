using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyApplication.Api.Clients;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;

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
    public async Task NoServiceToken_ReturnsFalse_WithoutSendingAnything()
    {
        var gateway = new FakeGateway();

        var sent = await MakeClient(gateway, tokens: new MissingTokenProvider()).SendWarningNoticeAsync(Notice);

        Assert.False(sent);
        Assert.Empty(gateway.Requests);
    }

    // TC-TRN-06
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task GatewayUnreachable_ReturnsFalse_InsteadOfThrowing()
    {
        Assert.False(await MakeClient(new UnreachableHandler()).SendWarningNoticeAsync(Notice));
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
