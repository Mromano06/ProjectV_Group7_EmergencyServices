using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using MyApplication.Api.Auth;
using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// The normal test API, plus a pretend Registration login and a known JWT signing key,
/// so POST /auth/login can make real tokens we can check.
/// </summary>
public class LoginApiFactory : EmergencyApiFactory
{
    public FakeRegistrationLoginClient Registration { get; } = new();

    public LoginApiFactory()
    {
        // Made-up accounts – one per Registration role, like their real test accounts.
        Registration.AddAccount("test.manager", "pw-manager", "Manager", "Terry Tester");
        Registration.AddAccount("test.dispatcher", "pw-dispatcher", "Dispatcher");
        Registration.AddAccount("test.user", "pw-user", "User");
        Registration.AddAccount("test.resident", "pw-resident", "Resident"); // a role with no Emergency mapping
        Registration.AddAccount("test.twohats", "pw-twohats", ["User", "Dispatcher"]); // several roles
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRegistrationLoginClient>();
            services.AddSingleton<IRegistrationLoginClient>(Registration);

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.TokenValidationParameters.ValidIssuers = [EmergencyTokenIssuerTests.Issuer];
                o.TokenValidationParameters.ValidAudiences = [EmergencyTokenIssuerTests.Audience];
                o.TokenValidationParameters.IssuerSigningKeys = [EmergencyTokenIssuerTests.Key];
            });
        });
    }
}

/// <summary>
/// SH-S01: signing in to the console with a Registration account through POST /api/emergency/v1/auth/login.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Area", "Login and roles")]
public class AuthApiTests : IClassFixture<LoginApiFactory>
{
    private const string LoginUrl = "/api/emergency/v1/auth/login";
    private readonly LoginApiFactory _factory;

    public AuthApiTests(LoginApiFactory factory)
    {
        _factory = factory;
        _factory.Registration.Down = false;
    }

    private Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        _factory.CreateClient().PostAsJsonAsync(LoginUrl, new { username, password });

    // TC-API-AUTH-01
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task Manager_SignsIn_AndGetsAnAdminToken()
    {
        var response = await LoginAsync("test.manager", "pw-manager");

        var body = await response.ExpectAsync<LoginResponse>(HttpStatusCode.OK);
        Assert.Equal("emergency:Admin", body.Role);
        Assert.Equal("Manager", body.RegistrationRole);
        Assert.Equal("Terry Tester", body.Name);
        Assert.Equal("Bearer", body.TokenType);
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);

        var token = new JsonWebToken(body.AccessToken);
        Assert.Equal(EmergencyTokenIssuerTests.Issuer, token.Issuer);
        Assert.Contains(EmergencyTokenIssuerTests.Audience, token.Audiences);
        Assert.Equal("emergency:Admin", token.GetClaim("role").Value);
    }

    // TC-API-AUTH-02
    [Theory]
    [InlineData("test.dispatcher", "pw-dispatcher", "emergency:Dispatcher")]
    [InlineData("test.user", "pw-user", "emergency:Viewer")]
    [Trait("Requirement", "SH-S01")]
    public async Task EachRegistrationRole_GetsItsEmergencyRole(string username, string password, string expected)
    {
        var body = await (await LoginAsync(username, password)).ExpectAsync<LoginResponse>(HttpStatusCode.OK);

        Assert.Equal(expected, body.Role);
        Assert.Equal(expected, new JsonWebToken(body.AccessToken).GetClaim("role").Value);
    }

    // TC-API-AUTH-03
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task UsernameIsNotCaseSensitive_AndSpacesAreTrimmed()
    {
        await (await LoginAsync("  TEST.Manager ", "pw-manager")).ExpectAsync(HttpStatusCode.OK);
    }

    // TC-API-AUTH-04
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task WrongPassword_Is401_InvalidCredentials()
    {
        var code = await (await LoginAsync("test.manager", "not-the-password")).ExpectErrorAsync(HttpStatusCode.Unauthorized);

        Assert.Equal("invalid_credentials", code);
    }

    // TC-API-AUTH-05
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task UnknownUser_Is401_InvalidCredentials()
    {
        var code = await (await LoginAsync("nobody", "pw")).ExpectErrorAsync(HttpStatusCode.Unauthorized);

        Assert.Equal("invalid_credentials", code);
    }

    // TC-API-AUTH-06
    [Fact]
    [Trait("Requirement", "SH-S02")]
    public async Task RegistrationRoleWithNoEmergencyAccess_Is403()
    {
        var code = await (await LoginAsync("test.resident", "pw-resident")).ExpectErrorAsync(HttpStatusCode.Forbidden);

        Assert.Equal("no_emergency_role", code);
    }

    // TC-API-AUTH-07
    [Fact]
    [Trait("Requirement", "SH-A07")]
    public async Task RegistrationDown_Is503_RegistrationUnavailable()
    {
        _factory.Registration.Down = true;

        var code = await (await LoginAsync("test.manager", "pw-manager")).ExpectErrorAsync(HttpStatusCode.ServiceUnavailable);

        Assert.Equal("registration_unavailable", code);
    }

    // TC-API-AUTH-08
    [Fact]
    [Trait("Requirement", "SH-I03")]
    public async Task MissingPassword_Is400_ValidationFailed()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(LoginUrl, new { username = "test.manager" });

        Assert.Equal("validation_failed", await response.ExpectErrorAsync(HttpStatusCode.BadRequest));
    }

    // TC-API-AUTH-11
    [Fact]
    [Trait("Requirement", "SH-S02")]
    public async Task SeveralRegistrationRoles_GetTheStrongestEmergencyRole()
    {
        var body = await (await LoginAsync("test.twohats", "pw-twohats")).ExpectAsync<LoginResponse>(HttpStatusCode.OK);

        Assert.Equal("emergency:Dispatcher", body.Role);   // Dispatcher beats User (Viewer)
        Assert.Equal("User, Dispatcher", body.RegistrationRole);
    }

    // TC-API-AUTH-09
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task LoginReply_NeverContainsThePassword()
    {
        var response = await LoginAsync("test.manager", "pw-manager");

        await response.ExpectAsync(HttpStatusCode.OK);
        Assert.DoesNotContain("pw-manager", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Password guessing is slowed down: the 21st try in a minute gets 429.</summary>

[Trait("Category", "Integration")]
[Trait("Area", "Login and roles")]
public class AuthRateLimitApiTests : IClassFixture<LoginApiFactory>
{
    private readonly LoginApiFactory _factory;

    public AuthRateLimitApiTests(LoginApiFactory factory) => _factory = factory;

    // TC-API-AUTH-10
    [Fact]
    [Trait("Requirement", "SH-Q02")]
    public async Task TooManySignInTries_Is429_TooManyAttempts()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < LoginRateLimit.AttemptsPerMinute; i++)
        {
            await (await client.PostAsJsonAsync("/api/emergency/v1/auth/login",
                new { username = "test.manager", password = "wrong" })).ExpectAsync(HttpStatusCode.Unauthorized);
        }

        var last = await client.PostAsJsonAsync("/api/emergency/v1/auth/login",
            new { username = "test.manager", password = "pw-manager" });

        Assert.Equal("too_many_attempts", await last.ExpectErrorAsync(HttpStatusCode.TooManyRequests));
    }
}
