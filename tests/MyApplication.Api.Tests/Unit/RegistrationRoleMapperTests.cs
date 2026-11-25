using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using MyApplication.Api.Auth;

namespace MyApplication.Api.Tests;

/// <summary>
/// Registration's plain roles (Manager, Dispatcher, User) become our emergency:* roles.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Login and roles")]
public class RegistrationRoleMapperTests
{
    private static readonly Dictionary<string, string?> TeamMapping = new()
    {
        ["RoleMapping:Manager"] = "emergency:Admin",
        ["RoleMapping:Dispatcher"] = "emergency:Dispatcher",
        ["RoleMapping:User"] = "emergency:Viewer",
    };

    private static RegistrationRoleMapper Mapper(Dictionary<string, string?>? settings = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings ?? TeamMapping).Build());

    private static ClaimsPrincipal LoggedIn(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "Bearer"));

    // TC-ROLE-01
    [Theory]
    [Trait("Requirement", "SH-S02")]
    [InlineData("Manager", "emergency:Admin")]
    [InlineData("Dispatcher", "emergency:Dispatcher")]
    [InlineData("User", "emergency:Viewer")]
    [InlineData("dispatcher", "emergency:Dispatcher")] // names are not case-sensitive
    public async Task RegistrationRole_GetsTheMatchingEmergencyRole(string registrationRole, string emergencyRole)
    {
        var result = await Mapper().TransformAsync(LoggedIn(registrationRole));

        Assert.True(result.IsInRole(emergencyRole));
        Assert.True(result.IsInRole(registrationRole)); // the original role is kept
    }

    // TC-ROLE-02
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task OtherServicesRoles_GetNoEmergencyAccess()
    {
        var result = await Mapper().TransformAsync(LoggedIn("Police Officer", "Administrator", "Sergeant"));

        Assert.DoesNotContain(result.Claims, c => c.Value.StartsWith("emergency:"));
    }

    // TC-ROLE-03
    [Fact]
    [Trait("Requirement", "SH-S02")]
    public async Task ShortRoleClaimNames_AreUnderstoodToo()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "Manager")], "Bearer"));

        var result = await Mapper().TransformAsync(principal);

        Assert.True(result.IsInRole("emergency:Admin"));
    }

    // TC-ROLE-04
    [Fact]
    [Trait("Requirement", "SH-S02")]
    public async Task RunningTwice_DoesNotAddTheRoleTwice_AndNeverChangesTheOriginal()
    {
        var original = LoggedIn("Dispatcher");

        var once = await Mapper().TransformAsync(original);
        var twice = await Mapper().TransformAsync(once);

        Assert.Single(twice.Claims, c => c.Value == "emergency:Dispatcher");
        Assert.False(original.IsInRole("emergency:Dispatcher"));
    }

    // TC-ROLE-05
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task NotLoggedIn_OrNoMapping_ChangesNothing()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Manager")])); // no auth type

        Assert.Same(anonymous, await Mapper().TransformAsync(anonymous));
        var user = LoggedIn("Manager");
        Assert.Same(user, await Mapper(new Dictionary<string, string?>()).TransformAsync(user));
    }
}
