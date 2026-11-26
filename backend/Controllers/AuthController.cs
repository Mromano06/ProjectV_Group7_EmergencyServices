using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyApplication.Api.Auth;
using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Controllers;

/// <summary>
/// SH-S01: sign in to the dispatch console with a city account from Registration.
///
///   1. The console sends { username, password } here.
///   2. We check them with Registration, as its Auth Guide says: authenticate, then validate
///      (RegistrationLoginClient). Registration checks its own token; we need no secret of theirs.
///   3. We turn Registration's roles into ours with "RoleMapping" in appsettings.json
///      (Manager -> emergency:Admin, Dispatcher -> emergency:Dispatcher, User -> emergency:Viewer).
///      With several roles, the strongest Emergency role wins.
///   4. We give back OUR OWN token (EmergencyTokenIssuer), which the console uses for every call.
///
/// Errors: 400 validation_failed, 401 invalid_credentials, 403 no_emergency_role,
/// 429 too_many_attempts, 502/503/504 registration_unavailable, 500 login_not_configured.
/// Sign-ins and failed sign-ins are logged with the user ID / username and time, never the password (SH-Q02).
/// </summary>
[ApiController]
[Route("api/emergency/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(
    IRegistrationLoginClient registration,
    EmergencyTokenIssuer tokens,
    IConfiguration config,
    ILogger<AuthController> log) : OperationsControllerBase
{
    /// <summary>Sign in with a Registration username and password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest body, CancellationToken ct)
    {
        var username = body.Username.Trim();

        RegistrationLogin? login;
        try
        {
            login = await registration.AuthenticateAsync(username, body.Password, ct);
        }
        catch (ExternalServiceException ex)
        {
            log.LogWarning("Sign-in for {Username} at {Time:o} could not be checked: {Reason}", username, DateTime.UtcNow, ex.Message);
            return Error(ex.HttpStatus is 502 or 503 or 504 ? ex.HttpStatus : 502, "registration_unavailable",
                "The city login service (Registration) is not responding. Try again in a minute.");
        }

        if (login is null)
        {
            log.LogWarning("Failed sign-in for {Username} at {Time:o}: wrong username or password", username, DateTime.UtcNow);
            return Error(StatusCodes.Status401Unauthorized, "invalid_credentials", "Wrong username or password.");
        }

        var registrationRoles = string.Join(", ", login.Roles);
        var role = EmergencyRoleFor(login.Roles);
        if (role is null)
        {
            log.LogWarning("Sign-in refused for user {UserId} ({Username}) at {Time:o}: Registration role(s) {Roles} have no Emergency access",
                login.UserId, login.Username, DateTime.UtcNow, registrationRoles);
            return Error(StatusCodes.Status403Forbidden, "no_emergency_role",
                $"Your account's role ({registrationRoles}) does not have access to Emergency Services.");
        }

        IssuedToken token;
        try
        {
            token = tokens.Issue(login, role);
        }
        catch (InvalidOperationException ex)
        {
            log.LogError(ex, "Sign-in for {Username} failed: no JWT signing key configured", login.Username);
            return Error(StatusCodes.Status500InternalServerError, "login_not_configured",
                "The Emergency API has no signing key set up (JWT_SIGNING_KEY in .env).");
        }

        log.LogInformation("User {UserId} ({Username}) signed in at {Time:o} as {Role} (Registration role(s) {RegistrationRoles})",
            login.UserId, login.Username, DateTime.UtcNow, role, registrationRoles);

        return Ok(new LoginResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc,
            login.DisplayName, login.Username, role, registrationRoles));
    }

    /// <summary>Strongest first: someone with several roles gets the most access any of them gives.</summary>
    private static readonly string[] Strength =
        ["emergency:Admin", "emergency:Dispatcher", "emergency:Responder", "emergency:Viewer"];

    /// <summary>
    /// Registration's roles -> our strongest emergency:* role, using "RoleMapping" in appsettings.json.
    /// A role that is already "emergency:..." is kept. No match at all gets no access (null).
    /// </summary>
    private string? EmergencyRoleFor(IEnumerable<string> registrationRoles)
    {
        var map = config.GetSection("RoleMapping").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!.Trim(), StringComparer.OrdinalIgnoreCase);

        var mapped = registrationRoles
            .Select(r => r.StartsWith("emergency:", StringComparison.OrdinalIgnoreCase) ? r : map.GetValueOrDefault(r))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        if (mapped.Count == 0) return null;

        return mapped.OrderBy(r =>
        {
            var i = Array.FindIndex(Strength, s => s.Equals(r, StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }).First();
    }
}
