namespace MyApplication.Api.Clients;

// Matthew Romano - Nov 14th, 2026 - Token Provider 

// TODO: change when doing integration
// Gets the token for auth automatically for dev
/// <summary>Replace with Registration's service-account token acquisition for production.</summary>
public interface IServiceTokenProvider
{
    Task<string> GetTokenAsync(CancellationToken ct);
}
