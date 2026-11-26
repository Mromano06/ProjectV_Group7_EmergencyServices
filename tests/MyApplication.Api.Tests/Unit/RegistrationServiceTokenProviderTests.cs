using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MyApplication.Api.Clients;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// SH-S07: our Registration service-account token (sent to Transit's POST /road-closures, Budget and HR).
/// The provider logs in with the service account's username/password, reuses the token until shortly
/// before it expires, and never sends the password anywhere but Registration.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "External services")]
public class RegistrationServiceTokenProviderTests
{
    private const string LoginPath = "/" + RegistrationLoginClient.DefaultLoginPath;

    private static RegistrationServiceTokenProvider MakeProvider(
        FakeGateway gateway, string? username = "svc-emergency", string? password = "secret", string? fixedToken = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceAccount:Username"] = username,
            ["ServiceAccount:Password"] = password,
            ["ServiceAccount:AccessToken"] = fixedToken,
        }).Build();
        return new RegistrationServiceTokenProvider(new GatewayClients(gateway), config,
            NullLogger<RegistrationServiceTokenProvider>.Instance);
    }

    /// <summary>A JWT-shaped string with the given exp (signature not checked here).</summary>
    private static string Jwt(DateTimeOffset expires)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"HS256\"}")}.{B64($"{{\"sub\":\"svc\",\"exp\":{expires.ToUnixTimeSeconds()}}}")}.sig";
    }

    // TC-SVC-01
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task LogsInToRegistration_WithTheServiceAccount_AndReturnsItsToken()
    {
        var gateway = new FakeGateway();
        var token = Jwt(DateTimeOffset.UtcNow.AddHours(1));
        gateway.Respond(HttpMethod.Post, LoginPath, HttpStatusCode.OK, new { accessToken = token });

        Assert.Equal(token, await MakeProvider(gateway).GetTokenAsync(CancellationToken.None));

        var login = Assert.Single(gateway.Requests);
        Assert.Contains("\"username\":\"svc-emergency\"", login.Body);
        Assert.Contains("\"password\":\"secret\"", login.Body);
    }

    // TC-SVC-02
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task ReusesTheToken_UntilItIsAboutToExpire()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, LoginPath, HttpStatusCode.OK, new { accessToken = Jwt(DateTimeOffset.UtcNow.AddHours(1)) });
        var provider = MakeProvider(gateway);

        await provider.GetTokenAsync(CancellationToken.None);
        await provider.GetTokenAsync(CancellationToken.None);
        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Single(gateway.Requests); // logged in once
    }

    // TC-SVC-03
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task LogsInAgain_WhenTheTokenIsAboutToExpire()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, LoginPath, HttpStatusCode.OK, new { accessToken = Jwt(DateTimeOffset.UtcNow.AddSeconds(30)) });
        var provider = MakeProvider(gateway);

        await provider.GetTokenAsync(CancellationToken.None);
        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, gateway.Requests.Count); // under a minute left -> renewed
    }

    // TC-SVC-04
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task APastedToken_IsUsed_WithoutLoggingIn()
    {
        var gateway = new FakeGateway();

        Assert.Equal("pasted", await MakeProvider(gateway, fixedToken: "pasted").GetTokenAsync(CancellationToken.None));
        Assert.Empty(gateway.Requests);
    }

    // TC-SVC-05
    [Theory]
    [InlineData(null, "secret")]
    [InlineData("svc-emergency", null)]
    [Trait("Requirement", "SH-S07")]
    public async Task NoServiceAccountConfigured_Throws_SoCallersSendWithoutAToken(string? username, string? password)
    {
        var gateway = new FakeGateway();

        await Assert.ThrowsAsync<ExternalServiceException>(() =>
            MakeProvider(gateway, username, password).GetTokenAsync(CancellationToken.None));
        Assert.Empty(gateway.Requests);
    }

    // TC-SVC-06
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [Trait("Requirement", "SH-A07")]
    public async Task RegistrationRefuses_Throws_WithoutLeakingThePassword(HttpStatusCode status)
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, LoginPath, status, new { error = "no" });

        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() =>
            MakeProvider(gateway).GetTokenAsync(CancellationToken.None));
        Assert.DoesNotContain("secret", ex.Message);
    }

    // TC-SVC-07
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public void ReadsExpiryFromTheJwt_AndIgnoresTokensThatArentJwts()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30);

        Assert.Equal(expires.ToUnixTimeSeconds(), RegistrationServiceTokenProvider.ExpiryOf(Jwt(expires))!.Value.ToUnixTimeSeconds());
        Assert.Null(RegistrationServiceTokenProvider.ExpiryOf("not-a-jwt"));
    }

    /// <summary>Hands out HttpClients that talk to the fake gateway.</summary>
    private sealed class GatewayClients(FakeGateway gateway) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(gateway, disposeHandler: false) { BaseAddress = new Uri(FakeGateway.BaseUrl) };
    }
}