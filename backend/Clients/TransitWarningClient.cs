using MyApplication.Api.Models;

namespace MyApplication.Api.Clients;

/// <summary>
/// Talks to the Public Transport service about service-affecting warnings.
/// </summary>
public interface ITransitWarningClient
{
    /// <summary>Returns true if Transit accepted the notice (any 2xx response).</summary>
    Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice);
}

public class TransitWarningClient : ITransitWarningClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<TransitWarningClient> _logger;

    public TransitWarningClient(HttpClient http, IConfiguration config, ILogger<TransitWarningClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendWarningNoticeAsync(TransitWarningNotice notice)
    {
        // The address comes from appsettings / environment variables (SH-A06), never hard-coded.
        // Transit hasn't confirmed their endpoint yet, so the path is a setting too.
        var path = _config["Services:TransitWarningPath"];

        if (_http.BaseAddress == null || string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("Transit URL not configured – warning {Id} was not sent to Public Transport.", notice.WarningId);
            return false;
        }

        try
        {
            var response = await _http.PostAsJsonAsync(path, notice);
            if (response.IsSuccessStatusCode) return true;

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