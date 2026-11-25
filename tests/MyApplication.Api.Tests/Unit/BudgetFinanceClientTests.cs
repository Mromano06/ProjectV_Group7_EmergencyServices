using System.Net;
using MyApplication.Api.Clients;
using MyApplication.Api.Dtos;
using MyApplication.Api.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// BudgetFinanceClient against Budget's live contract (Swagger, Nov 25).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "External services")]
public class BudgetFinanceClientTests
{
    private static BudgetFinanceClient Client(FakeGateway gateway, IServiceTokenProvider tokens) =>
        new(new HttpClient(gateway) { BaseAddress = new Uri(FakeGateway.BaseUrl) }, tokens);

    private sealed class NoTokenProvider : IServiceTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken ct) =>
            throw new ExternalServiceException("Service-account token is not configured.");
    }

    // TC-BUD-01
    [Fact]
    [Trait("Requirement", "SH-C07")]
    public async Task Allocation_IsReadByDepartmentCode_EMS()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/EMS", HttpStatusCode.OK,
            new { departmentCode = "EMS", allocated = 4500000m, spent = 0m, remaining = 4500000m, currency = "CAD" });

        var json = await Client(gateway, new FakeServiceTokenProvider()).GetAllocationAsync(CancellationToken.None);

        Assert.Equal(4500000m, json.GetProperty("remaining").GetDecimal());
        Assert.Equal("/api/budget/v1/allocations/EMS", Assert.Single(gateway.Requests).PathAndQuery);
    }

    // TC-BUD-02
    [Fact]
    [Trait("Requirement", "SH-C08")]
    public async Task WithoutAServiceToken_TheCallStillGoesOut_WithNoAuthorizationHeader()
    {
        // Budget doesn't require a token yet, and Registration has no service accounts yet.
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Post, "/api/budget/v1/expenses", HttpStatusCode.Created, new { approved = true });

        await Client(gateway, new NoTokenProvider())
            .SubmitExpenseAsync(new ExpenseRequest("EMS", 25m, "Fuel"), CancellationToken.None);

        var sent = Assert.Single(gateway.Requests);
        Assert.Null(sent.Authorization);
        Assert.Contains("\"departmentCode\":\"EMS\"", sent.Body);
    }

    // TC-BUD-03
    [Fact]
    [Trait("Requirement", "SH-S07")]
    public async Task WithAServiceToken_ItIsSent()
    {
        var gateway = new FakeGateway();
        gateway.Respond(HttpMethod.Get, "/api/budget/v1/allocations/EMS", HttpStatusCode.OK, new { allocated = 1m });

        await Client(gateway, new FakeServiceTokenProvider()).GetAllocationAsync(CancellationToken.None);

        Assert.Equal($"Bearer {FakeServiceTokenProvider.Token}", Assert.Single(gateway.Requests).Authorization);
    }
}
