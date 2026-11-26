using System.Net;
using Microsoft.Extensions.Configuration;
using MyApplication.Api.Clients;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// RegistrationLoginClient, following Registration's "CM-RS-API Auth Guide" (Nov 25):
///   POST /api/login/authenticate { username, password, rememberMe } -> { accessToken, ... }
///   POST /api/login/validate (Bearer accessToken) -> { authenticated, userId, username, roles, claims }
/// The replies below have the same shape as Registration's real ones. All accounts are made up.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Login and roles")]
public class RegistrationLoginClientTests
{
    private const string LoginPath = "/api/login/authenticate";
    private const string ValidatePath = "/api/login/validate";
    private const string RegistrationToken = "header.payload.signature";

    private static RegistrationLoginClient Client(FakeGateway gateway) =>
        new(new HttpClient(gateway) { BaseAddress = new Uri(FakeGateway.BaseUrl) },
            new ConfigurationBuilder().Build());

    private static object LoginReply => new
    {
        accessToken = RegistrationToken, tokenType = "Bearer", role = "Manager",
        expiresAtUtc = "2030-01-01T12:00:00Z",
    };

    /// <summary>Shaped like Registration's real validate reply (Nov 25).</summary>
    private static object ValidateReply(string[]? roles = null, bool authenticated = true, bool withNames = true) => new
    {
        authenticated,
        userId = "6fca88ca-0000-4000-8000-000000000001",
        username = "test.manager",
        roles = roles ?? ["Manager"],
        claims = new List<object>
        {
            new { type = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", value = "6fca88ca-0000-4000-8000-000000000001" },
            new { type = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", value = "test.manager" },
            new { type = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", value = (roles ?? ["Manager"])[0] },
            new { type = "exp", value = "1893499200" }, // 2030-01-01T12:00:00Z
            new { type = "iss", value = "CmRegistrationService" },
        }.Concat(withNames
            ? new object[]
            {
                new { type = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname", value = "Terry" },
                new { type = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname", value = "Tester " }, // Registration sends a trailing space
            }
            : []),
    };

    private static FakeGateway Registration(HttpStatusCode loginStatus = HttpStatusCode.OK, object? login = null,
        HttpStatusCode validateStatus = HttpStatusCode.OK, object? validate = null)
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, LoginPath, loginStatus, login ?? LoginReply);
        gateway.Respond(HttpMethod.Post, ValidatePath, validateStatus, validate ?? ValidateReply());
        return gateway;
    }

    // TC-REG-LOGIN-01
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task GoodLogin_IsValidated_AndWhoSignedInComesFromValidate()
    {
        var login = await Client(Registration()).AuthenticateAsync("test.manager", "pw-1", CancellationToken.None);

        Assert.NotNull(login);
        Assert.Equal(new[] { "Manager" }, login!.Roles);
        Assert.Equal("test.manager", login.Username);
        Assert.Equal("Terry Tester", login.DisplayName);
        Assert.Equal("6fca88ca-0000-4000-8000-000000000001", login.UserId);
        Assert.Equal(new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc), login.ExpiresAtUtc); // from "exp"
    }

    // TC-REG-LOGIN-02
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task SendsTheLogin_ThenValidatesRegistrationsToken_AsTheAuthGuideSays()
    {
        var gateway = Registration();

        await Client(gateway).AuthenticateAsync("test.user", "pw-2", CancellationToken.None);

        Assert.Equal(2, gateway.Requests.Count);
        var authenticate = gateway.Requests[0];
        Assert.Equal(("POST", LoginPath), (authenticate.Method, authenticate.PathAndQuery));
        Assert.Null(authenticate.Authorization);
        Assert.Contains("\"username\":\"test.user\"", authenticate.Body);
        Assert.Contains("\"password\":\"pw-2\"", authenticate.Body);
        Assert.Contains("\"rememberMe\":false", authenticate.Body);

        var validate = gateway.Requests[1];
        Assert.Equal(("POST", ValidatePath), (validate.Method, validate.PathAndQuery));
        Assert.Equal($"Bearer {RegistrationToken}", validate.Authorization); // Registration's token, checked by Registration
    }

    // TC-REG-LOGIN-03
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [Trait("Requirement", "SH-S01")]
    public async Task WrongUsernameOrPassword_ReturnsNull_AndNothingIsValidated(HttpStatusCode status)
    {
        var gateway = Registration(loginStatus: status, login: new { message = "Invalid username or password." });

        Assert.Null(await Client(gateway).AuthenticateAsync("test.user", "wrong", CancellationToken.None));
        Assert.Single(gateway.Requests);
    }

    // TC-REG-LOGIN-04
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task ValidateSaysNotAuthenticated_ReturnsNull()
    {
        var gateway = Registration(validate: ValidateReply(authenticated: false));

        Assert.Null(await Client(gateway).AuthenticateAsync("test.manager", "pw", CancellationToken.None));
    }

    // TC-REG-LOGIN-05
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task ValidateRefusesTheToken_ReturnsNull()
    {
        var gateway = Registration(validateStatus: HttpStatusCode.Unauthorized, validate: new { message = "Invalid token" });

        Assert.Null(await Client(gateway).AuthenticateAsync("test.manager", "pw", CancellationToken.None));
    }

    // TC-REG-LOGIN-06
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.OK)]  // authenticate broken
    [InlineData(HttpStatusCode.OK, HttpStatusCode.InternalServerError)]  // validate broken
    [Trait("Requirement", "SH-A07")]
    public async Task RegistrationBroken_Throws502(HttpStatusCode loginStatus, HttpStatusCode validateStatus)
    {
        var gateway = Registration(loginStatus: loginStatus, validateStatus: validateStatus);

        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() =>
            Client(gateway).AuthenticateAsync("test.user", "pw", CancellationToken.None));
        Assert.Equal(502, ex.HttpStatus);
    }

    // TC-REG-LOGIN-07
    [Fact]
    [Trait("Requirement", "SH-A06")]
    public async Task NoRegistrationUrl_Throws503_WithoutSendingAnything()
    {
        var gateway = new FakeGateway();
        var client = new RegistrationLoginClient(new HttpClient(gateway), new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() =>
            client.AuthenticateAsync("test.user", "pw", CancellationToken.None));
        Assert.Equal(503, ex.HttpStatus);
        Assert.Empty(gateway.Requests);
    }

    // TC-REG-LOGIN-08
    [Fact]
    [Trait("Requirement", "SH-S02")]
    public async Task SeveralRoles_AreAllKept_AndNoNames_FallsBackToTheUsername()
    {
        var gateway = Registration(validate: ValidateReply(roles: ["User", "Dispatcher"], withNames: false));

        var login = await Client(gateway).AuthenticateAsync("test.manager", "pw", CancellationToken.None);

        Assert.Equal(new[] { "User", "Dispatcher" }, login!.Roles);
        Assert.Equal("test.manager", login.DisplayName);
    }

    // TC-REG-LOGIN-09
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task ValidateWithNoRoleAtAll_Throws502()
    {
        var gateway = Registration(validate: new { authenticated = true, userId = "u1", username = "x", roles = Array.Empty<string>(), claims = Array.Empty<object>() });

        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() =>
            Client(gateway).AuthenticateAsync("x", "pw", CancellationToken.None));
        Assert.Equal(502, ex.HttpStatus);
    }

    // TC-REG-LOGIN-10
    [Fact]
    [Trait("Requirement", "SH-S01")]
    public async Task LoginReplyWithNoToken_Throws502()
    {
        var gateway = Registration(login: new { tokenType = "Bearer" });

        var ex = await Assert.ThrowsAsync<ExternalServiceException>(() =>
            Client(gateway).AuthenticateAsync("x", "pw", CancellationToken.None));
        Assert.Equal(502, ex.HttpStatus);
    }
}
