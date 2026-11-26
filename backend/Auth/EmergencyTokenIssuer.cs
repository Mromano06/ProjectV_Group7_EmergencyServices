using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApplication.Api.Clients;

namespace MyApplication.Api.Auth;

/// <summary>Our own login token for the console, and when it runs out.</summary>
public sealed record IssuedToken(string AccessToken, DateTime ExpiresAtUtc);

/// <summary>
/// After Registration confirms a username and password, this makes OUR API's own token for the console.
/// It uses the same key, issuer and audience our API already checks (the "Authentication:Schemes:Bearer"
/// settings: JWT_SIGNING_KEY in Docker, or `dotnet user-jwts` locally), so no new secret is needed
/// and every [Authorize] rule keeps working.
///
/// The token holds: sub (Registration user ID), unique_name (username), name (display name),
/// role (our emergency:* role) and registration_role (Registration's roles, for reference).
/// It lasts until Registration's own login runs out, and never more than 60 minutes (SH-S01).
/// </summary>
public sealed class EmergencyTokenIssuer(IOptionsMonitor<JwtBearerOptions> jwtOptions)
{
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(60);

    /// <exception cref="InvalidOperationException">No signing key, issuer or audience is configured.</exception>
    public IssuedToken Issue(RegistrationLogin login, string emergencyRole)
    {
        var p = jwtOptions.Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

        var key = p.IssuerSigningKeys?.OfType<SymmetricSecurityKey>().FirstOrDefault()
                  ?? p.IssuerSigningKey as SymmetricSecurityKey;
        var issuer = p.ValidIssuers?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i)) ?? p.ValidIssuer;
        var audience = p.ValidAudiences?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a)) ?? p.ValidAudience;

        if (key is null || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            throw new InvalidOperationException(
                "No JWT signing key, issuer or audience is configured (Authentication:Schemes:Bearer).");

        var now = DateTime.UtcNow;
        var expires = now + MaxLifetime;
        if (login.ExpiresAtUtc is { } registrationEnds && registrationEnds > now && registrationEnds < expires)
            expires = registrationEnds;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = login.UserId,
                ["unique_name"] = login.Username,
                ["name"] = login.DisplayName,
                ["role"] = emergencyRole,
                ["registration_role"] = string.Join(",", login.Roles),
            },
        };

        return new IssuedToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
