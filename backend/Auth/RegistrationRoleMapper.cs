using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace MyApplication.Api.Auth;

/// <summary>
/// Registration's login gives plain role names (Manager, Dispatcher, User) instead of the
/// "emergency:Role" names from SH-S02. Our team chose to keep those names, so after a token is
/// validated this adds the matching Emergency role, and every [Authorize] rule keeps working:
///
///   Manager    -> emergency:Admin
///   Dispatcher -> emergency:Dispatcher
///   User       -> emergency:Viewer
///
/// The mapping lives in appsettings.json ("RoleMapping"), so it can change without code.
/// Note: plain names are shared by all services, so e.g. a Security "Dispatcher" also gets
/// Emergency dispatch rights. Switch Registration to prefixed roles to remove that overlap.
/// </summary>
public sealed class RegistrationRoleMapper(IConfiguration config) : IClaimsTransformation
{
    /// <summary>Claim types a JWT may use for roles (standard, short and plural forms).</summary>
    private static readonly string[] RoleClaimTypes = [ClaimTypes.Role, "role", "roles"];

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var map = config.GetSection("RoleMapping").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
        if (map.Count == 0 || principal.Identity is not ClaimsIdentity { IsAuthenticated: true })
            return Task.FromResult(principal);

        var toAdd = principal.Claims
            .Where(c => RoleClaimTypes.Contains(c.Type))
            .Select(c => c.Value.Trim())
            .Where(map.ContainsKey)
            .Select(role => map[role])
            .Where(role => !principal.IsInRole(role)) // already there: nothing to do (runs once per request)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (toAdd.Count == 0) return Task.FromResult(principal);

        // Never change the original principal; return a copy with the extra roles.
        // ClaimsPrincipal.Clone() would share the same identity objects, so copy each identity too.
        var copy = new ClaimsPrincipal(principal.Identities.Select(i => i.Clone()));
        var identity = (ClaimsIdentity)copy.Identity!;
        foreach (var role in toAdd)
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        return Task.FromResult(copy);
    }
}