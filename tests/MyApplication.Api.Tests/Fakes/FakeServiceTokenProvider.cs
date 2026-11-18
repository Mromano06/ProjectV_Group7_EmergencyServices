using MyApplication.Api.Clients;

namespace MyApplication.Api.Tests;

/// <summary>
/// Stands in for the service-account token our API attaches when it calls other
/// services (SH-S07). Tests can check the token reached the other service.
/// </summary>
public class FakeServiceTokenProvider : IServiceTokenProvider
{
    public const string Token = "test-service-token";

    public Task<string> GetTokenAsync(CancellationToken ct) => Task.FromResult(Token);
}
