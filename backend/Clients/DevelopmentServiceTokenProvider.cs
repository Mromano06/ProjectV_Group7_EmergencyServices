using MyApplication.Api.Integration;
namespace MyApplication.Api.Clients;

// Matthew Romano - Nov 14th, 2026 - Development 

// TODO: Update this to work in final imp
/// <summary>
/// DEVELOPMENT ONLY. Reads a service-account bearer token from user secrets/env.
/// Real deployments must request/refresh short-lived tokens through Registration.
/// </summary>
public sealed class DevelopmentServiceTokenProvider(IConfiguration configuration) : IServiceTokenProvider
{
    public Task<string> GetTokenAsync(CancellationToken ct)
    {
        string? token = configuration["ServiceAccount:AccessToken"];
        if (string.IsNullOrWhiteSpace(token))
            throw new ExternalServiceException("Service-account token is not configured.");
        return Task.FromResult(token);
    }
}
