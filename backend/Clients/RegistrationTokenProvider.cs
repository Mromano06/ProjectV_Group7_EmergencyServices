using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Clients;

/// <summary>
/// SH-S07: gets the Registration Bearer token that we send to other teams. Transit's Integration Guide
/// (Nov 26) requires it on POST /road-closures: log in to Registration with admin/service-level credentials,
/// send the accessToken as "Authorization: Bearer ...", reuse it until it expires, then log in again.
/// Budget and HR get the same token.
///
/// Where the token comes from, first match wins:
///   1. ServiceAccount:AccessToken  – a token pasted in by hand (SERVICE_ACCOUNT_TOKEN in .env). Handy for testing,
///                                    but it stops working when it expires.
///   2. ServiceAccount:Username + ServiceAccount:Password (SERVICE_ACCOUNT_USERNAME / SERVICE_ACCOUNT_PASSWORD in .env)
///                                  – we log in to Registration's POST api/login/authenticate, the same login the
///                                    console uses (RegistrationLoginClient), and keep the token until shortly before
///                                    its "exp", then log in again.
/// With neither, <see cref="GetTokenAsync"/> throws <see cref="ExternalServiceException"/>; the clients catch it and
/// send without a token.
///
/// Registered as a singleton so every request shares one cached token instead of logging in each time.
/// The password and token are never logged.
/// </summary>
public sealed class RegistrationServiceTokenProvider(
    IHttpClientFactory httpClients, IConfiguration config, ILogger<RegistrationServiceTokenProvider> logger)
    : IServiceTokenProvider
{
    /// <summary>Named HttpClient pointed at Registration (set up in Program.cs).</summary>
    public const string HttpClientName = "RegistrationServiceAccount";

    /// <summary>Log in again this long before the token's exp, so it can't run out mid-call.</summary>
    public static readonly TimeSpan RefreshEarly = TimeSpan.FromMinutes(1);

    /// <summary>How long to keep a token whose exp we can't read.</summary>
    public static readonly TimeSpan FallbackLifetime = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _refreshAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        var fixedToken = config["ServiceAccount:AccessToken"];
        if (!string.IsNullOrWhiteSpace(fixedToken)) return fixedToken.Trim();

        var username = config["ServiceAccount:Username"];
        var password = config["ServiceAccount:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new ExternalServiceException(
                "Service-account login is not configured (ServiceAccount:Username and ServiceAccount:Password).");

        if (_token is not null && DateTimeOffset.UtcNow < _refreshAt) return _token;

        await _lock.WaitAsync(ct);
        try
        {
            // Another request may have logged in while this one waited.
            if (_token is not null && DateTimeOffset.UtcNow < _refreshAt) return _token;

            var token = await LoginAsync(username.Trim(), password, ct);
            var expires = ExpiryOf(token) ?? DateTimeOffset.UtcNow + FallbackLifetime;
            _token = token;
            _refreshAt = expires - RefreshEarly;
            logger.LogInformation("Service account {User} logged in to Registration; token valid until {Expires:u}.",
                username, expires);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> LoginAsync(string username, string password, CancellationToken ct)
    {
        var http = httpClients.CreateClient(HttpClientName);
        if (http.BaseAddress is null)
            throw new ExternalServiceException("Registration's URL is not configured (Services:RegistrationBaseUrl).");

        var path = config["Services:RegistrationLoginPath"];
        using var request = new HttpRequestMessage(HttpMethod.Post,
            string.IsNullOrWhiteSpace(path) ? RegistrationLoginClient.DefaultLoginPath : path)
        {
            Content = JsonContent.Create(new { username, password, rememberMe = false }),
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceException("Registration did not answer the service-account login in time.", 504);
        }
        catch (HttpRequestException)
        {
            throw new ExternalServiceException("Could not reach Registration for the service-account login.", 503);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException(
                    $"Registration refused the service-account login (HTTP {(int)response.StatusCode}).", 502);

            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in doc.RootElement.EnumerateObject())
                        if (string.Equals(p.Name, "accessToken", StringComparison.OrdinalIgnoreCase) &&
                            p.Value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(p.Value.GetString()))
                            return p.Value.GetString()!;
            }
            catch (JsonException)
            {
                // falls through to the error below
            }
            throw new ExternalServiceException("Registration's service-account login reply had no access token.", 502);
        }
    }

    /// <summary>
    /// The token's "exp" claim, read without checking the signature (we only use it to know when to
    /// log in again; Transit checks the signature). Null if it isn't a JWT or has no exp.
    /// </summary>
    public static DateTimeOffset? ExpiryOf(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("exp", out var exp) &&
                exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            // not a readable JWT
        }
        return null;
    }
}