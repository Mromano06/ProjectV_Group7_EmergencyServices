namespace MyApplication.Api.Integration;

// Matthew Romano - Nov 14th, 2026 - External Services Handling

// Small class to handle external services without leaking secrets
/// <summary>Small outbound transport error; don't leak upstream bodies or secrets.</summary>
public sealed class ExternalServiceException(string message, int httpStatus = 503) : Exception(message)
{
    public int HttpStatus { get; } = httpStatus;
}
