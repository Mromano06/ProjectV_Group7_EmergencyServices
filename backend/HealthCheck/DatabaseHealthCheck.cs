using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MyApplication.Api.Data;

namespace MyApplication.Api.Health;

/// <summary>
/// Part of GET /health (ES-NFR-001, SH-A05): healthy only when our own database answers.
/// Gives up after 5 seconds, like our calls to other services (SH-A07).
/// </summary>
public sealed class DatabaseHealthCheck(EmergencyDbContext db) : IHealthCheck
{
    /// <summary>How long to wait for the database before reporting it as down.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Tries to connect to the database; never throws.</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            // The exception is kept for the server log only; the response never shows it,
            // because it can contain the server name or connection details.
            return HealthCheckResult.Unhealthy("Database is not reachable.", ex);
        }
    }
}

/// <summary>
/// Writes the /health response as JSON, e.g.
/// { "status": "Healthy", "service": "emergency", "checks": { "database": "Healthy" }, "checkedAt": "...Z" }.
/// Healthy = HTTP 200, Unhealthy = HTTP 503 (ASP.NET's defaults).
/// </summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Only names and statuses go out – no exception messages or connection details.</summary>
    public static Task WriteAsync(HttpContext http, HealthReport report)
    {
        http.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString(),
            service = "emergency",
            checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
            checkedAt = DateTime.UtcNow // SH-I04
        };
        return http.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
