using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MyApplication.Api.Auth;

/// <summary>
/// SH-S01 / SH-S03: users log in through Registration, which issues the JWT, and our API validates
/// that token on every request. This adds Registration's issuer, audience and key to the token
/// checks, next to the dev tokens from `dotnet user-jwts` (those keep working for local testing).
///
/// Settings ("Registration" section, or Registration__* environment variables in Docker):
///   Issuer     – default CmRegistrationService
///   Audience   – default CmRegistrationClients
///   PublicKey  – RS256 public key (PEM). Safe to share; preferred.
///   SigningKey – HS256 shared secret. SECRET: only in the root .env, never in the repo.
/// Set one of PublicKey or SigningKey. With neither, Registration tokens are not accepted yet.
/// </summary>
public static class RegistrationTokens
{
    public const string DefaultIssuer = "CmRegistrationService";
    public const string DefaultAudience = "CmRegistrationClients";

    /// <summary>The key that checks Registration's signature, or null if none is configured.</summary>
    /// <exception cref="InvalidOperationException">Registration:PublicKey is set but is not a valid PEM key.</exception>
    public static SecurityKey? SigningKey(IConfiguration config)
    {
        var pem = config["Registration:PublicKey"];
        if (!string.IsNullOrWhiteSpace(pem))
        {
            try
            {
                var rsa = RSA.Create();
                // A .env file holds the PEM on one line, with "\n" written where the line breaks go.
                rsa.ImportFromPem(pem.Replace("\\n", "\n"));
                return new RsaSecurityKey(rsa);
            }
            catch (Exception ex) when (ex is ArgumentException or CryptographicException)
            {
                throw new InvalidOperationException(
                    "Registration:PublicKey (REGISTRATION_PUBLIC_KEY) is not a valid PEM public key.", ex);
            }
        }

        var secret = config["Registration:SigningKey"];
        return string.IsNullOrWhiteSpace(secret) ? null : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    public static string Issuer(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Registration:Issuer"]) ? DefaultIssuer : config["Registration:Issuer"]!.Trim();

    public static string Audience(IConfiguration config) =>
        string.IsNullOrWhiteSpace(config["Registration:Audience"]) ? DefaultAudience : config["Registration:Audience"]!.Trim();

    /// <summary>
    /// Adds Registration's issuer, audience and key to the token checks, keeping the ones already there.
    /// Returns false (and changes nothing) when no Registration key is configured.
    /// </summary>
    public static bool Accept(TokenValidationParameters p, IConfiguration config)
    {
        var key = SigningKey(config);
        if (key is null) return false;

        p.ValidIssuers = Combine(p.ValidIssuers, p.ValidIssuer, Issuer(config));
        p.ValidAudiences = Combine(p.ValidAudiences, p.ValidAudience, Audience(config));
        p.IssuerSigningKeys = [
            .. p.IssuerSigningKeys ?? [],
            .. p.IssuerSigningKey is null ? Array.Empty<SecurityKey>() : [p.IssuerSigningKey],
            key,
        ];
        return true;
    }

    /// <summary>One line for the startup log saying whether Registration tokens are accepted.</summary>
    public static string Describe(IConfiguration config) => SigningKey(config) switch
    {
        RsaSecurityKey => $"Accepting Registration tokens: issuer {Issuer(config)}, audience {Audience(config)}, RS256 public key.",
        SymmetricSecurityKey => $"Accepting Registration tokens: issuer {Issuer(config)}, audience {Audience(config)}, HS256 secret.",
        _ => "Registration's own tokens are NOT accepted directly (not needed: the console signs in through /auth/login, which checks with Registration).",
    };

    private static string[] Combine(IEnumerable<string>? list, string? single, string extra) =>
        (list ?? []).Append(single ?? "").Append(extra)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
