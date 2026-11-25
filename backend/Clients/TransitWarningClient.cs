using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Clients;

/// <summary>
/// Talks to the Public Transport service: weather warnings and road-restricting incidents.
/// </summary>
public interface ITransitWarningClient
{
    /// <summary>Returns true if Transit accepted the notice (any 2xx response).</summary>
    Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice);

    /// <summary>ES-FR-015: tell Transit an incident is blocking roads. True if Transit accepted it.</summary>
    Task<bool> SendIncidentNoticeAsync(TransitIncidentNotice notice);
}

/// <summary>
/// Sends notices to Public Transport the same way our other integrations work:
///   - through the API gateway (SH-A03), or Transit's own URL until the gateway exists
///   - with our service-account token (SH-S07), so Transit knows the call is from Emergency Services
///
/// It never throws: if Transit, the gateway or the token isn't available it logs why and
/// returns false, so our own work still succeeds when Transit is down (SH-A07).
/// </summary>
public class TransitWarningClient : ITransitWarningClient
{
    /// <summary>Weather notices. Confirmed by Transit (Nov 25).</summary>
    public const string DefaultPath = "api/transit/v1/disruptions/weather-notices";

    /// <summary>Road-restricting incident notices (SH-C13). Confirmed by Transit (Nov 25).</summary>
    public const string DefaultIncidentPath = "api/transit/v1/disruptions/emergency-notices";

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

    /// <summary>sends a road-restricting incident notice to transit (ES-FR-015)</summary>
    public Task<bool> SendIncidentNoticeAsync(TransitIncidentNotice notice) =>
        SendAsync(PathOr("Services:TransitIncidentNoticePath", DefaultIncidentPath), notice, $"incident {notice.IncidentId}");

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

        string token;
        try
        {
            token = await _tokens.GetTokenAsync(CancellationToken.None);
        }
        catch (ExternalServiceException ex)
        {
            _logger.LogWarning("No service-account token ({Reason}) – {What} was not sent to Public Transport.",
                ex.Message, what);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning("Transit rejected {What}: HTTP {Status}", what, (int)response.StatusCode);
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
