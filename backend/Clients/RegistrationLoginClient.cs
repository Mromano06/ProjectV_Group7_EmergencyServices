using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Clients;

/// <summary>Who Registration says signed in, from its validate endpoint.</summary>
/// <param name="UserId">Registration's user ID.</param>
/// <param name="Username">The username they signed in with.</param>
/// <param name="DisplayName">First + last name if Registration has them, otherwise the username.</param>
/// <param name="Roles">Registration's role names, e.g. Manager, Dispatcher, User.</param>
/// <param name="ExpiresAtUtc">When Registration's login runs out (its token's "exp"), if it said.</param>
public sealed record RegistrationLogin(
    string UserId, string Username, string DisplayName, IReadOnlyList<string> Roles, DateTime? ExpiresAtUtc);

/// <summary>Checks a username and password with Registration (SH-S01).</summary>
public interface IRegistrationLoginClient
{
    /// <summary>
    /// Returns who signed in, or null if Registration says the username or password is wrong.
    /// Throws <see cref="ExternalServiceException"/> if Registration can't be reached or answers strangely.
    /// </summary>
    Task<RegistrationLogin?> AuthenticateAsync(string username, string password, CancellationToken ct);
}

/// <summary>
/// SH-S01, the way Registration's "CM-RS-API Auth Guide" (Nov 25) tells every team to do it:
///
///   1. POST {Registration}/api/login/authenticate  { username, password, rememberMe } -> { accessToken, ... }
///   2. POST {Registration}/api/login/validate      Authorization: Bearer {accessToken}
///      -> { authenticated, userId, username, roles: [...], claims: [{ type, value }] }
///   3. Our API turns that into its own login for the console (AuthController + EmergencyTokenIssuer).
///
/// Registration checks the token in step 2, so we need no secret or key of theirs.
/// Their token never leaves our API, and the password is never logged or stored.
/// </summary>
public sealed class RegistrationLoginClient(HttpClient http, IConfiguration config) : IRegistrationLoginClient
{
    /// <summary>Registration's live API (Nov 25). Used when Services:RegistrationBaseUrl isn't set.</summary>
    public const string DefaultBaseUrl = "https://cm-rs-api-gbgrffa8h5e5fdcy.canadaeast-01.azurewebsites.net/";
    public const string DefaultLoginPath = "api/login/authenticate";
    public const string DefaultValidatePath = "api/login/validate";

    private const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string GivenNameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname";
    private const string SurnameClaim = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname";

    public async Task<RegistrationLogin?> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        if (http.BaseAddress is null)
            throw new ExternalServiceException("Registration's URL is not configured (Services:RegistrationBaseUrl).", 503);

        // 1. authenticate: is the password right?
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, Path("Services:RegistrationLoginPath", DefaultLoginPath))
        {
            Content = JsonContent.Create(new { username, password, rememberMe = false }),
        };
        var login = await SendAsync(loginRequest, ct);
        if (login is null) return null; // wrong username or password
        var accessToken = GetString(login.Value, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ExternalServiceException("Registration's login reply had no access token.", 502);

        // 2. validate: Registration checks its own token and tells us who it is.
        using var validateRequest = new HttpRequestMessage(HttpMethod.Post, Path("Services:RegistrationValidatePath", DefaultValidatePath));
        validateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var validation = await SendAsync(validateRequest, ct);
        if (validation is null) return null;
        if (!(validation.Value.TryGetProperty("authenticated", out var ok) && ok.ValueKind == JsonValueKind.True))
            return null;

        return FromValidation(validation.Value, username)
               ?? throw new ExternalServiceException("Registration's validate reply was not understood.", 502);
    }

    private string Path(string key, string fallback) =>
        string.IsNullOrWhiteSpace(config[key]) ? fallback : config[key]!;

    /// <summary>
    /// Sends one request. Returns the JSON body on 2xx, or null when Registration says the login is
    /// wrong (400 / 401 / 403). Anything else becomes an <see cref="ExternalServiceException"/>.
    /// </summary>
    private async Task<JsonElement?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExternalServiceException("Registration did not answer within 5 seconds.", 504);
        }
        catch (HttpRequestException)
        {
            throw new ExternalServiceException("Could not reach Registration.", 503);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new ExternalServiceException($"Registration returned HTTP {(int)response.StatusCode}.", 502);

            var text = await response.Content.ReadAsStringAsync(ct);
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ExternalServiceException("Registration's reply was not understood.", 502);
                return doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new ExternalServiceException("Registration's reply was not understood.", 502);
            }
        }
    }

    /// <summary>Who signed in, from validate's reply. Null if it names no role at all.</summary>
    private static RegistrationLogin? FromValidation(JsonElement v, string typedUsername)
    {
        var claims = new List<(string Type, string Value)>();
        if (v.TryGetProperty("claims", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var c in list.EnumerateArray())
                if (c.ValueKind == JsonValueKind.Object && GetString(c, "type") is { } t && GetString(c, "value") is { } val)
                    claims.Add((t, val));
        string? Claim(string type) => claims.FirstOrDefault(c => c.Type == type).Value;

        var roles = new List<string>();
        if (v.TryGetProperty("roles", out var r) && r.ValueKind == JsonValueKind.Array)
            roles.AddRange(r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()));
        if (roles.Count == 0)
            roles.AddRange(claims.Where(c => c.Type is RoleClaim or "role").Select(c => c.Value.Trim()));
        roles = roles.Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roles.Count == 0) return null;

        var username = GetString(v, "username") ?? typedUsername;
        var name = string.Join(' ', new[] { Claim(GivenNameClaim)?.Trim(), Claim(SurnameClaim)?.Trim() }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        DateTime? expires = long.TryParse(Claim("exp"), out var exp)
            ? DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime
            : null;

        return new RegistrationLogin(
            UserId: GetString(v, "userId") ?? username,
            Username: username,
            DisplayName: name.Length > 0 ? name : username,
            Roles: roles,
            ExpiresAtUtc: expires);
    }

    /// <summary>A string property (case-insensitive name).</summary>
    private static string? GetString(JsonElement obj, string name)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : null;
        return null;
    }
}
