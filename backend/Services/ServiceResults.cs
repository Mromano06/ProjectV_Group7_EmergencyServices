namespace MyApplication.Api.Services;

// Matthew Romano - Nov 14th, 2026 - Formats Services Results

// Keeps results for induvidual services
/// <summary>Explicit service outcome, so controllers can map to SH-I03 consistently.</summary>
public sealed record ServiceResult<T>(T? Value, int HttpStatus, string? Error = null, string? Message = null)
{
    public static ServiceResult<T> Ok(T value) => new(value, 200);
    public static ServiceResult<T> Created(T value) => new(value, 201);
    public static ServiceResult<T> Invalid(string code, string message) => new(default, 400, code, message);
    public static ServiceResult<T> Missing(string code, string message) => new(default, 404, code, message);
    public static ServiceResult<T> Conflict(string code, string message) => new(default, 409, code, message);
}
