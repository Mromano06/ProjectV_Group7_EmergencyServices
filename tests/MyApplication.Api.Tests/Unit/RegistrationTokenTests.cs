using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApplication.Api.Auth;

namespace MyApplication.Api.Tests;

/// <summary>
/// SH-S01 / SH-S03: our API accepts tokens issued by Registration (HS256 secret or RS256 public key),
/// and dev tokens from `dotnet user-jwts` keep working. All keys here are made up for the tests.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Login and roles")]
public class RegistrationTokensTests
{
    public const string TestSecret = "made-up-registration-secret-for-tests-only-32+";
    private const string RoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private static readonly SymmetricSecurityKey DevKey =
        new(Encoding.UTF8.GetBytes("made-up-dotnet-user-jwts-key-for-tests-only"));

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    /// <summary>The token checks as our API has them before Registration is added (dev tokens only).</summary>
    private static TokenValidationParameters DevOnly() => new()
    {
        ValidIssuers = ["dotnet-user-jwts"],
        ValidAudiences = ["http://localhost:5227"],
        IssuerSigningKeys = [DevKey],
    };

    /// <summary>A token shaped like Registration's: plain role under the Microsoft role claim, 60 minutes.</summary>
    public static string RegistrationToken(SecurityKey key, string role = "Manager",
        string issuer = RegistrationTokens.DefaultIssuer, string audience = RegistrationTokens.DefaultAudience,
        DateTime? expires = null)
    {
        var ends = expires ?? DateTime.UtcNow.AddMinutes(60);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = ends.AddMinutes(-60),
            NotBefore = ends.AddMinutes(-60),
            Expires = ends,
            SigningCredentials = new SigningCredentials(key,
                key is RsaSecurityKey ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "2b9a6f0e-0000-4000-8000-000000000001",
                ["unique_name"] = "test.manager",
                [RoleClaim] = role,
            },
        });
    }

    private static async Task<bool> IsValid(string token, TokenValidationParameters p) =>
        (await new JsonWebTokenHandler().ValidateTokenAsync(token, p)).IsValid;

    // TC-REG-TOKEN-01
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task WithRegistrationsSecret_TheirTokenIsAccepted_WithItsRole()
    {
        var p = DevOnly();
        Assert.True(RegistrationTokens.Accept(p, Config(("Registration:SigningKey", TestSecret))));

        var token = RegistrationToken(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, p);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal("Manager", result.Claims[RoleClaim]);
    }

    // TC-REG-TOKEN-02
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task DevTokens_StillWork_AfterRegistrationIsAdded()
    {
        var p = DevOnly();
        RegistrationTokens.Accept(p, Config(("Registration:SigningKey", TestSecret)));

        var dev = RegistrationToken(DevKey, "emergency:Admin", "dotnet-user-jwts", "http://localhost:5227");

        Assert.True(await IsValid(dev, p));
    }

    // TC-REG-TOKEN-03
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task TokenSignedWithAnotherSecret_IsRejected()
    {
        var p = DevOnly();
        RegistrationTokens.Accept(p, Config(("Registration:SigningKey", TestSecret)));

        var forged = RegistrationToken(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("someone-elses-secret-that-is-32-bytes-long")));

        Assert.False(await IsValid(forged, p));
    }

    // TC-REG-TOKEN-04
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task WithRegistrationsPublicKey_RS256TokensAreAccepted_AndOthersRejected()
    {
        using var registration = RSA.Create(2048);
        using var someoneElse = RSA.Create(2048);
        // As it would sit on one line in .env
        var pem = registration.ExportSubjectPublicKeyInfoPem().Replace("\n", "\\n");

        var p = DevOnly();
        Assert.True(RegistrationTokens.Accept(p, Config(("Registration:PublicKey", pem))));

        Assert.True(await IsValid(RegistrationToken(new RsaSecurityKey(registration.ExportParameters(true))), p));
        Assert.False(await IsValid(RegistrationToken(new RsaSecurityKey(someoneElse.ExportParameters(true))), p));
    }

    // TC-REG-TOKEN-05
    [Theory]
    [InlineData("SomeOtherService", RegistrationTokens.DefaultAudience)]
    [InlineData(RegistrationTokens.DefaultIssuer, "SomeOtherClients")]
    [Trait("Requirement", "SH-S03")]
    public async Task WrongIssuerOrAudience_IsRejected(string issuer, string audience)
    {
        var p = DevOnly();
        RegistrationTokens.Accept(p, Config(("Registration:SigningKey", TestSecret)));

        var token = RegistrationToken(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            issuer: issuer, audience: audience);

        Assert.False(await IsValid(token, p));
    }

    // TC-REG-TOKEN-06
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task ExpiredRegistrationToken_IsRejected()
    {
        var p = DevOnly();
        RegistrationTokens.Accept(p, Config(("Registration:SigningKey", TestSecret)));

        var token = RegistrationToken(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret)),
            expires: DateTime.UtcNow.AddMinutes(-10));

        Assert.False(await IsValid(token, p));
    }

    // TC-REG-TOKEN-07
    [Fact]
    [Trait("Requirement", "SH-A06")]
    public void NoKeyConfigured_ChangesNothing()
    {
        var p = DevOnly();

        Assert.False(RegistrationTokens.Accept(p, Config()));
        Assert.Equal(new[] { "dotnet-user-jwts" }, p.ValidIssuers);
        Assert.Single(p.IssuerSigningKeys);
        Assert.Contains("NOT accepted", RegistrationTokens.Describe(Config()));
    }

    // TC-REG-TOKEN-08
    [Fact]
    [Trait("Requirement", "SH-A06")]
    public void InvalidPublicKey_GivesAClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RegistrationTokens.SigningKey(Config(("Registration:PublicKey", "not a key"))));

        Assert.Contains("REGISTRATION_PUBLIC_KEY", ex.Message);
    }
}
