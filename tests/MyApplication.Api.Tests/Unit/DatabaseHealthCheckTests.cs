using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MyApplication.Api.Data;
using MyApplication.Api.Health;
using MyApplication.Api.Tests.Integration;

namespace MyApplication.Api.Tests;

/// <summary>
/// The database part of GET /health, tested against a database that is up and one that is down.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Health")]
public class DatabaseHealthCheckTests
{
    private static EmergencyDbContext ContextFor(string connectionString) =>
        new SqliteTestDbContext(new DbContextOptionsBuilder<EmergencyDbContext>()
            .UseSqlite(connectionString).Options);

    private static Task<HealthCheckResult> RunAsync(EmergencyDbContext db) =>
        new DatabaseHealthCheck(db).CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", _ => null!, null, null)
        });

    // TC-HLT-04
    [Fact]
    [Trait("Requirement", "ES-NFR-001")]
    public async Task DatabaseCheck_IsHealthy_WhenTheDatabaseAnswers()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var db = new SqliteTestDbContext(new DbContextOptionsBuilder<EmergencyDbContext>()
            .UseSqlite(connection).Options);

        var result = await RunAsync(db);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // TC-HLT-05
    [Fact]
    [Trait("Requirement", "SH-A05")]
    public async Task DatabaseCheck_IsUnhealthy_AndDoesNotThrow_WhenTheDatabaseIsMissing()
    {
        // A read-only connection to a file in a folder that doesn't exist can never open.
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        using var db = ContextFor($"Data Source={missing};Mode=ReadOnly");

        var result = await RunAsync(db);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
