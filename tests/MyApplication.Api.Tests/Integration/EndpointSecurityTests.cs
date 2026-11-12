using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// WHOLE-BACKEND checks. These don't know about any one feature – they find every
/// controller endpoint in the API automatically (warnings, hospitals, incidents, units...)
/// and check the rules every endpoint must follow. When a teammate adds a new controller,
/// it is covered by these tests with no extra work.
///
/// If one fails, the message lists exactly which endpoints broke the rule.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Whole backend")]
public class EndpointSecurityTests : IClassFixture<EmergencyApiFactory>
{
    private readonly EmergencyApiFactory _factory;

    public EndpointSecurityTests(EmergencyApiFactory factory)
    {
        _factory = factory;
    }

    // TC-API-ALL-01  (SH-S03: every service validates the JWT on every request)
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public void EveryEndpoint_HasAnExplicitSecurityRule()
    {
        var missing = ControllerEndpoints()
            .Where(e => !RequiresLogin(e) && e.Metadata.GetMetadata<IAllowAnonymous>() == null)
            .Select(Describe)
            .ToList();

        Assert.True(missing.Count == 0,
            "These endpoints have no [Authorize] (and no deliberate [AllowAnonymous]), so anyone can call them:\n  "
            + string.Join("\n  ", missing)
            + "\nFix: add [Authorize] (or [Authorize(Roles = \"emergency:...\")]) to the controller or action.");
    }

    // TC-API-ALL-02  (SH-S03, ES-NFR-003: 401 when not logged in)
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task EveryProtectedEndpoint_Returns401_WhenNotLoggedIn()
    {
        var client = _factory.CreateClientAs(role: null);
        var endpoints = ControllerEndpoints().Where(RequiresLogin).ToList();
        Assert.NotEmpty(endpoints);

        var failures = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var response = await client.SendAsync(BuildRequest(endpoint));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"{Describe(endpoint)} -> {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        Assert.True(failures.Count == 0,
            "These endpoints should return 401 when no one is logged in, but didn't:\n  " + string.Join("\n  ", failures));
    }

    // TC-API-ALL-03  (ES-NFR-003: no unexpected 500s; Admin can do "everything" per the Roles sheet)
    [Fact]
    [Trait("Requirement", "ES-NFR-003")]
    public async Task EveryListEndpoint_WorksForAdmin_WithoutServerErrors()
    {
        var client = _factory.CreateClientAs(Roles.Admin);

        // GET endpoints with no {id} in the URL, e.g. GET /hospitals, GET /hospitals/summary
        var listEndpoints = ControllerEndpoints()
            .Where(e => HttpMethodOf(e) == "GET" && e.RoutePattern.Parameters.Count == 0)
            .ToList();
        Assert.NotEmpty(listEndpoints);

        var failures = new List<string>();
        foreach (var endpoint in listEndpoints)
        {
            var response = await client.SendAsync(BuildRequest(endpoint));
            var code = response.StatusCode;

            // 500 = the code crashed. 401/403 = Admin was refused, but Admin can do everything.
            // (502/503 are allowed: that's how we report another city service being down.)
            if (code is HttpStatusCode.InternalServerError or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var body = await response.Content.ReadAsStringAsync();
                failures.Add($"{Describe(endpoint)} -> {(int)code} {code} {Shorten(body)}");
            }
        }

        Assert.True(failures.Count == 0,
            "These GET endpoints failed for an Admin on an empty database:\n  " + string.Join("\n  ", failures));
    }

    // TC-API-ALL-04  (SH-I01: routes follow /api/emergency/v1/{resource})
    [Fact]
    [Trait("Requirement", "SH-I01")]
    public void EveryEndpoint_FollowsTheSharedRouteFormat()
    {
        var wrong = ControllerEndpoints()
            .Where(e => !(e.RoutePattern.RawText ?? "").TrimStart('/')
                .StartsWith("api/emergency/v1/", StringComparison.OrdinalIgnoreCase))
            .Select(Describe)
            .ToList();

        Assert.True(wrong.Count == 0,
            "These endpoints don't start with /api/emergency/v1/ (SH-I01), so the API gateway won't route to them:\n  "
            + string.Join("\n  ", wrong));
    }

    // TC-API-ALL-05  (SH-I02: API docs are published)
    [Fact]
    [Trait("Requirement", "SH-I02")]
    public async Task ApiDocumentation_IsPublished()
    {
        var client = _factory.CreateClientAs(role: null);

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/emergency/v1/", json);
    }

    // TC-API-ALL-06
    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var client = _factory.CreateClientAs(Roles.Admin);

        var response = await client.GetAsync("/api/emergency/v1/this-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    /// <summary>Every endpoint that comes from a controller (skips /openapi etc.).</summary>
    private List<RouteEndpoint> ControllerEndpoints()
    {
        _ = _factory.CreateClient(); // makes sure the API has started
        return _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() != null)
            .ToList();
    }

    private static bool RequiresLogin(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any()
        && e.Metadata.GetMetadata<IAllowAnonymous>() == null;

    private static string HttpMethodOf(RouteEndpoint e) =>
        e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.FirstOrDefault() ?? "GET";

    private static string Describe(RouteEndpoint e) => $"{HttpMethodOf(e)} /{(e.RoutePattern.RawText ?? "").TrimStart('/')}";

    /// <summary>Turns a route like "api/emergency/v1/units/{id:guid}" into a real URL we can call.</summary>
    private static HttpRequestMessage BuildRequest(RouteEndpoint e)
    {
        var url = "/" + Regex.Replace((e.RoutePattern.RawText ?? "").TrimStart('/'), @"\{([^}]*)\}", match =>
        {
            var constraints = match.Groups[1].Value.TrimStart('*').Split(':').Skip(1)
                .Select(c => c.ToLowerInvariant()).ToList();

            if (constraints.Any(c => c.StartsWith("guid"))) return Guid.NewGuid().ToString();
            if (constraints.Any(c => c.StartsWith("int") || c.StartsWith("long") || c.StartsWith("min") || c.StartsWith("range"))) return "1";
            if (constraints.Any(c => c.StartsWith("bool"))) return "true";
            return "test";
        });

        var method = HttpMethodOf(e);
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = JsonContent.Create(new { });
        }
        return request;
    }

    private static string Shorten(string text) =>
        text.Length <= 200 ? text : text[..200] + "...";
}
