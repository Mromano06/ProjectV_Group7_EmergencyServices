using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// The normal test API, but logging in with REAL JWT checking (not the X-Test-Role shortcut)
/// and a made-up Registration secret, so a Registration-shaped token goes through the same
/// checks and role mapping as in production.
/// </summary>
public class RegistrationTokenApiFactory : EmergencyApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Registration:SigningKey", RegistrationTokensTests.TestSecret);
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)); // real JWT checks again
    }
}

/// <summary>SH-S01 / SH-S03: a token issued by Registration works against our real API, with the right access.</summary>
[Trait("Category", "Integration")]
[Trait("Area", "Login and roles")]
public class RegistrationTokenApiTests : IClassFixture<RegistrationTokenApiFactory>
{
    private const string Stations = "/api/emergency/v1/stations";              // Viewers get 403 (TC-API-STN-06)
    private const string Warnings = "/api/emergency/v1/weather-warnings?active=true"; // anyone logged in

    private static readonly SymmetricSecurityKey RegistrationKey =
        new(Encoding.UTF8.GetBytes(RegistrationTokensTests.TestSecret));

    private readonly RegistrationTokenApiFactory _factory;

    public RegistrationTokenApiTests(RegistrationTokenApiFactory factory) => _factory = factory;

    private Task<HttpResponseMessage> GetWithToken(string url, string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(url);
    }

    // TC-API-REG-TOKEN-01
    [Theory]
    [InlineData("Manager", HttpStatusCode.OK)]     // -> emergency:Admin
    [InlineData("Dispatcher", HttpStatusCode.OK)]  // -> emergency:Dispatcher
    [InlineData("User", HttpStatusCode.Forbidden)] // -> emergency:Viewer
    [Trait("Requirement", "SH-S01")]
    public async Task RegistrationToken_GetsTheMappedEmergencyAccess(string registrationRole, HttpStatusCode expected)
    {
        var token = RegistrationTokensTests.RegistrationToken(RegistrationKey, registrationRole);

        await (await GetWithToken(Stations, token)).ExpectAsync(expected);
    }

    // TC-API-REG-TOKEN-02
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task RegistrationUser_CanStillReadWarnings()
    {
        var token = RegistrationTokensTests.RegistrationToken(RegistrationKey, "User");

        await (await GetWithToken(Warnings, token)).ExpectAsync(HttpStatusCode.OK);
    }

    // TC-API-REG-TOKEN-03
    [Fact]
    [Trait("Requirement", "SH-S03")]
    public async Task ForgedToken_Is401()
    {
        var forged = RegistrationTokensTests.RegistrationToken(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("someone-elses-secret-that-is-32-bytes-long")));

        await (await GetWithToken(Warnings, forged)).ExpectAsync(HttpStatusCode.Unauthorized);
    }

    // TC-API-REG-TOKEN-04
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task ExpiredRegistrationToken_Is401()
    {
        var expired = RegistrationTokensTests.RegistrationToken(RegistrationKey, expires: DateTime.UtcNow.AddMinutes(-10));

        await (await GetWithToken(Warnings, expired)).ExpectAsync(HttpStatusCode.Unauthorized);
    }
}
