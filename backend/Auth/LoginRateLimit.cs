using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MyApplication.Api.Dtos;

namespace MyApplication.Api.Auth;

/// <summary>
/// Slows down password guessing on POST /auth/login: at most 20 tries a minute from one address.
/// After that the API answers 429 too_many_attempts until the minute is up.
/// (In Docker every browser comes through the same nginx address, so the limit is shared.)
/// </summary>
public static class LoginRateLimit
{
    public const string PolicyName = "login";
    public const int AttemptsPerMinute = 20;

    public static IServiceCollection AddLoginRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = AttemptsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            options.OnRejected = async (context, ct) =>
                await context.HttpContext.Response.WriteAsJsonAsync(new ErrorResponse(
                    "too_many_attempts",
                    "Too many sign-in attempts. Wait a minute and try again.",
                    context.HttpContext.TraceIdentifier), ct);
        });
}
