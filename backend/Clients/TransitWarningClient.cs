using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Clients;

/// <summary>
/// Talks to the Public Transport service: road-restricting incidents (ES-FR-015) and weather notices.
/// </summary>
public interface ITransitWarningClient
{
    /// <summary>Returns true if Transit accepted the notice (any 2xx response).</summary>
    Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice);

    /// <summary>
    /// ES-FR-015 / SH-C13 / PT-FR-019: create, update or clear a road closure for one of our incidents
    /// (POST /api/transport/v1/road-closures). True if Transit accepted it (201 created / 200 updated).
    /// Same ExternalId = same record on Transit's side.
    /// </summary>
    Task<bool> SendRoadClosureAsync(TransitRoadClosure closure);

    /// <summary>
    /// ES-FR-015: tells Transit the road is open again – same POST /road-closures, same external_id,
    /// status "resolved". True if Transit accepted it.
    /// </summary>
    Task<bool> ResolveRoadClosureAsync(TransitRoadClosureResolution resolution);
}

/// <summary>
/// Sends notices to Public Transport the same way our other integrations work:
///   - through the API gateway (SH-A03), or Transit's own URL until the gateway exists
///   - with the Bearer token from our Registration login (SH-S07). Transit's Integration Guide requires it
///     on POST /road-closures (no cookie or CSRF token). If we can't get a token the call still goes out
///     without one and Transit's 401 is logged, rather than not sending at all.
///
/// It never throws: if Transit or its URL isn't available it logs why and
/// returns false, so our own work still succeeds when Transit is down (SH-A07).
/// </summary>
public class TransitWarningClient : ITransitWarningClient
{
    /// <summary>
    /// Weather notices (team board extra, not a requirement). Transit's Swagger (Nov 25) has NO weather
    /// endpoint, so these are refused until Transit adds one. The weather requirement is the other way
    /// round: Transit reads GET /api/emergency/v1/weather-warnings?active=true from us (SH-C12, ES-FR-022).
    /// </summary>
    public const string DefaultPath = "api/transit/v1/disruptions/weather-notices";

    /// <summary>
    /// Road closures from our incidents: Transit's updated Swagger (Nov 26), PT-FR-019.
    /// Replaces the earlier api/transport/v1/integrations/emergency/incidents endpoint.
    /// Can be changed with "Services:TransitRoadClosurePath".
    /// </summary>
    public const string DefaultRoadClosurePath = "api/transport/v1/road-closures";

    private readonly HttpClient _http;
    private readonly IServiceTokenProvider _tokens;
    private readonly IConfiguration _config;
    private readonly ILogger<TransitWarningClient> _logger;

    public TransitWarningClient(
        HttpClient http,
        IServiceTokenProvider tokens,
        IConfiguration config,
        ILogger<TransitWarningClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _config = config;
        _logger = logger;
    }

    ///<summary>sends warning notice to transit client and gets response </summary>
    public Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice) =>
        SendAsync(PathOr("Services:TransitWarningPath", DefaultPath), notice, $"warning {notice.WarningId}");

    /// <summary>creates, updates or clears a road closure at transit (ES-FR-015, PT-FR-019)</summary>
    public Task<bool> SendRoadClosureAsync(TransitRoadClosure closure) =>
        SendAsync(PathOr("Services:TransitRoadClosurePath", DefaultRoadClosurePath), closure,
            $"road closure for incident {closure.ExternalId} ({closure.Status})");

    /// <summary>marks an incident's road closure resolved at transit (ES-FR-015)</summary>
    public Task<bool> ResolveRoadClosureAsync(TransitRoadClosureResolution resolution) =>
        SendAsync(PathOr("Services:TransitRoadClosurePath", DefaultRoadClosurePath), resolution,
            $"road closure for incident {resolution.ExternalId} ({resolution.Status})");

    private string PathOr(string key, string fallback)
    {
        var path = _config[key];
        return string.IsNullOrWhiteSpace(path) ? fallback : path;
    }

    private async Task<bool> SendAsync<T>(string path, T body, string what)
    {
        // The address comes from "Services:TransitBaseUrl" or "Services:GatewayBaseUrl" (set in Program.cs).
        if (_http.BaseAddress is null)
        {
            _logger.LogWarning("Transit/gateway URL not configured – {What} was not sent to Public Transport.", what);
            return false;
        }

        string? token = null;
        try
        {
            token = await _tokens.GetTokenAsync(CancellationToken.None);
        }
        catch (ExternalServiceException ex)
        {
            _logger.LogWarning("No service-account token ({Reason}) – sending {What} without one; Transit will likely answer 401.", ex.Message, what);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var reason = await response.Content.ReadAsStringAsync();
            if (reason.Length > 300) reason = reason[..300];
            // 401: token missing, invalid or expired. 403: the Registration account lacks a role Transit allows.
            // 400: a bad field value or timestamp, or a JSON field Transit doesn't know (they reject unknown fields).
            _logger.LogWarning("Transit rejected {What}: HTTP {Status} {Reason}", what, (int)response.StatusCode, reason);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException = the 5 second timeout ran out (SH-A07).
            _logger.LogWarning(ex, "Could not reach Transit for {What}", what);
            return false;
        }
    }
}