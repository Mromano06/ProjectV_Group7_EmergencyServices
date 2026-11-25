using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyApplication.Api.Integration;
using MyApplication.Api.Models;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Clients;

/// <summary>
/// Talks to the Public Transport service about service-affecting warnings.
/// </summary>
public interface ITransitWarningClient
{
    /// <summary>Returns true if Transit accepted the notice (any 2xx response).</summary>
    Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice);
}

/// <summary>
/// Sends warning notices to Public Transport the same way our other integrations work:
///   - through the API gateway (SH-A03), never straight to Transit's container
///   - with our service-account token (SH-S07), so Transit knows the call is from Emergency Services
///
/// It never throws: if Transit, the gateway or the token isn't available it logs why and
/// returns false, so issuing a warning still works when Transit is down (SH-A07).
/// </summary>
public class TransitWarningClient : ITransitWarningClient
{
    /// <summary>Used if "Services:TransitWarningPath" isn't set. Transit still has to confirm this path.</summary>
    public const string DefaultPath = "api/transit/v1/disruptions/weather-notices";

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
    public async Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice)
    {
        // The gateway address comes from "Services:GatewayBaseUrl" (set in Program.cs).
        if (_http.BaseAddress is null)
        {
            _logger.LogWarning("API gateway URL not configured – warning {Id} was not sent to Public Transport.", notice.WarningId);
            return false;
        }

        var path = _config["Services:TransitWarningPath"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = DefaultPath;
        }

        string token;
        try
        {
            token = await _tokens.GetTokenAsync(CancellationToken.None);
        }
        catch (ExternalServiceException ex)
        {
            _logger.LogWarning("No service-account token ({Reason}) – warning {Id} was not sent to Public Transport.",
                ex.Message, notice.WarningId);
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(notice)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            _logger.LogWarning("Transit rejected warning {Id}: HTTP {Status}", notice.WarningId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // TaskCanceledException = the 5 second timeout ran out (SH-A07).
            _logger.LogWarning(ex, "Could not reach Transit for warning {Id}", notice.WarningId);
            return false;
        }
    }
}
