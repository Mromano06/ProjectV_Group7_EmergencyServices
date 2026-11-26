using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApplication.Api.Auth;
using MyApplication.Api.Clients;

namespace MyApplication.Api.Tests;

/// <summary>
/// After Registration says a login is good, we make our own token. It must be signed with the key
/// our API checks, carry the emergency role, and never last longer than Registration's login or 60 minutes.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Login and roles")]
public class EmergencyTokenIssuerTests
{
    public const string Issuer = "emergency-tests";
    public const string Audience = "http://localhost:5227";
    public static readonly SymmetricSecurityKey Key =
        new(Encoding.UTF8.GetBytes("emergency-unit-tests-signing-key-32+bytes"));

    /// <summary>Just enough IOptionsMonitor to hand the issuer some JWT settings.</summary>
    private sealed class FixedOptions(JwtBearerOptions value) : IOptionsMonitor<JwtBearerOptions>
    {
        public JwtBearerOptions CurrentValue => value;
        public JwtBearerOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<JwtBearerOptions, string?> listener) => null;
    }

    private static EmergencyTokenIssuer MakeIssuer(SymmetricSecurityKey? key = null)
    {
        var options = new JwtBearerOptions();
        options.TokenValidationParameters.ValidIssuers = [Issuer];
        options.TokenValidationParameters.ValidAudiences = [Audience];
        if (key != null) options.TokenValidationParameters.IssuerSigningKeys = [key];
        return new EmergencyTokenIssuer(new FixedOptions(options));
    }

    private static RegistrationLogin Login(DateTime? expires = null) =>
        new("user-123", "test.manager", "Terry Tester", ["Manager"], expires);

    private static Task<TokenValidationResult> Validate(string token) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = Key,
        });

    // TC-AUTH-TOKEN-01
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task Token_IsSignedWithOurKey_AndCarriesTheEmergencyRoleAndName()
    {
        var issued = MakeIssuer(Key).Issue(Login(), "emergency:Admin");

        var result = await Validate(issued.AccessToken);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("emergency:Admin", result.Claims["role"]);
        Assert.Equal("Manager", result.Claims["registration_role"]);
        Assert.Equal("user-123", result.Claims["sub"]);
        Assert.Equal("test.manager", result.Claims["unique_name"]);
        Assert.Equal("Terry Tester", result.Claims["name"]);
    }

    // TC-AUTH-TOKEN-02
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task Token_SignedWithAnotherKey_IsRejected()
    {
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("some-other-key-that-is-also-32-bytes-long"));
        var issued = MakeIssuer(otherKey).Issue(Login(), "emergency:Admin");

        Assert.False((await Validate(issued.AccessToken)).IsValid);
    }

    // TC-AUTH-TOKEN-03
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public void Token_EndsWhenRegistrationsLoginEnds()
    {
        var registrationEnds = DateTime.UtcNow.AddMinutes(30);

        var issued = MakeIssuer(Key).Issue(Login(registrationEnds), "emergency:Viewer");

        Assert.Equal(registrationEnds, issued.ExpiresAtUtc);
    }

    // TC-AUTH-TOKEN-04
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public void Token_NeverLastsMoreThan60Minutes()
    {
        var issued = MakeIssuer(Key).Issue(Login(DateTime.UtcNow.AddDays(30)), "emergency:Viewer");

        Assert.InRange(issued.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(59), DateTime.UtcNow.AddMinutes(61));
    }

    // TC-AUTH-TOKEN-05
    [Fact]
    [Trait("Requirement", "SH-A06")]
    public void NoSigningKeyConfigured_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => MakeIssuer(key: null).Issue(Login(), "emergency:Admin"));
    }
}
