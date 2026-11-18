using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MyApplication.Api.Tests.Integration;

/// <summary>The role names exactly as they appear in a real JWT (SH-S02, Roles sheet).</summary>
public static class Roles
{
    public const string Admin = "emergency:Admin";
    public const string Dispatcher = "emergency:Dispatcher";
    public const string Responder = "emergency:Responder";
    public const string Viewer = "emergency:Viewer";
}

/// <summary>
/// A fake login used only in tests, so tests don't need real JWT tokens or signing keys.
///
///   X-Test-Role: emergency:Admin          -> a logged-in user with that role (comma-separate for several)
///   X-Test-Claims: service=security       -> extra claims, e.g. another city service's account
///   neither header                        -> not logged in (-> 401)
///
/// The API's own [Authorize] rules and policies are still applied, so 401/403 behaviour is real.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";
    public const string ClaimsHeader = "X-Test-Claims";

    /// <summary>The user ID every test request runs as (shows up in CreatedBy/UpdatedBy/ChangedBy).</summary>
    public static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var roles = Request.Headers[RoleHeader].ToString();
        var extraClaims = Request.Headers[ClaimsHeader].ToString();

        if (string.IsNullOrWhiteSpace(roles) && string.IsNullOrWhiteSpace(extraClaims))
        {
            return Task.FromResult(AuthenticateResult.NoResult()); // not logged in
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserId.ToString()),
            new(ClaimTypes.Name, "integration-test-user")
        };

        foreach (var role in roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // "type=value;type2=value2"
        foreach (var pair in extraClaims.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                claims.Add(new Claim(parts[0], parts[1]));
            }
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
