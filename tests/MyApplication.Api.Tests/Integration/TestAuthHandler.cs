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
/// The test sends a header like  X-Test-Role: emergency:Admin  and this handler treats the
/// request as coming from a logged-in user with that role. No header = not logged in (-> 401).
/// The API's own [Authorize(Roles = ...)] rules are still applied, so 401/403 behaviour is real.
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string RoleHeader = "X-Test-Role";

    /// <summary>The user ID every test request runs as (shows up in CreatedBy/UpdatedBy).</summary>
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
        if (!Request.Headers.TryGetValue(RoleHeader, out var header) || string.IsNullOrWhiteSpace(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult()); // not logged in
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, UserId.ToString()),
            new(ClaimTypes.Name, "integration-test-user")
        };

        // Allow several roles: "emergency:Admin,emergency:Dispatcher"
        foreach (var role in header.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
