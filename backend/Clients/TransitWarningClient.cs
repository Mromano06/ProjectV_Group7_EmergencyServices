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
    /// ES-FR-015 / SH-C13 / PT-FR-019: send (or update, or clear) a road-restricting incident.
    /// True if Transit accepted it. Same ExternalId = same record on Transit's side.
    /// </summary>
    Task<bool> SendEmergencyIncidentAsync(TransitEmergencyIncident incident);
}

/// <summary>
/// Sends notices to Public Transport the same way our other integrations work:
///   - through the API gateway (SH-A03), or Transit's own URL until the gateway exists
///   - with our service-account token (SH-S07) when we have one. Registration has no service
///     accounts yet, so until then calls go out without a token rather than not at all.
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

    /// <summary>Emergency incidents / road restrictions: Transit's Swagger (Nov 25), PT-FR-019.</summary>
    public const string DefaultEmergencyIncidentPath = "api/transport/v1/integrations/emergency/incidents";

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

    /// <summary>sends a road-restricting incident to transit (ES-FR-015, PT-FR-019)</summary>
    public Task<bool> SendEmergencyIncidentAsync(TransitEmergencyIncident incident) =>
        SendAsync(PathOr("Services:TransitEmergencyIncidentPath", DefaultEmergencyIncidentPath), incident,
            $"incident {incident.ExternalId} ({incident.Status})");

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
            _logger.LogInformation("No service-account token ({Reason}) – sending {What} without one.", ex.Message, what);
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
            // 401/403: Transit's write endpoints currently want an admin browser session (cookie + CSRF),
            // not a service token, so our server's calls are refused until Transit changes that.
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
