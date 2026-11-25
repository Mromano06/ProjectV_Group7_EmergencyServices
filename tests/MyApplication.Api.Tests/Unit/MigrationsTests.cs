using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MyApplication.Api.Data;

namespace MyApplication.Api.Tests;

/// <summary>
/// Guards the SQL Server migrations that docker compose runs on startup (Database__MigrateOnStartup).
///
/// The other tests build the test database straight from the model (EnsureCreated), so they
/// still pass when someone changes a model but forgets "dotnet ef migrations add". Then the
/// real database is missing tables and the API fails on startup in Docker. These tests catch that.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Database")]
public class MigrationTests
{
    /// <summary>
    /// A SQL Server context that is never opened: comparing the model to the migrations
    /// needs no database, so these tests run anywhere (no Docker, no SQL Server).
    /// </summary>
    private static EmergencyDbContext SqlServerContext() =>
        new(new DbContextOptionsBuilder<EmergencyDbContext>()
            .UseSqlServer("Server=localhost;Database=MigrationCheck;TrustServerCertificate=True")
            .Options);

    // TC-MIG-01
    [Fact]
    [Trait("Requirement", "SH-D01")]
    public void Model_HasNoChanges_MissingFromTheMigrations()
    {
        using var db = SqlServerContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model has changed since the last migration. From the backend folder run: " +
            "dotnet ef migrations add <DescribeYourChange>   and commit all three files it creates.");
    }

    // TC-MIG-02
    [Fact]
    [Trait("Requirement", "SH-D01")]
    public void EveryMigrationClass_IsRegistered_SoItActuallyRuns()
    {
        // EF silently SKIPS a migration class without [Migration] and [DbContext] attributes
        // (they live in the .Designer.cs file). That happens if the Designer file is lost
        // or the migration is copied by hand.
        var migrations = typeof(EmergencyDbContext).Assembly.GetTypes()
            .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(migrations);
        var notRegistered = migrations
            .Where(t => t.GetCustomAttribute<MigrationAttribute>() is null ||
                        t.GetCustomAttribute<DbContextAttribute>()?.ContextType != typeof(EmergencyDbContext))
            .Select(t => t.Name)
            .ToList();

        Assert.True(notRegistered.Count == 0,
            "Migration classes missing their .Designer.cs attributes: " + string.Join(", ", notRegistered));
    }

    // TC-MIG-03
    [Fact]
    [Trait("Requirement", "SH-D01")]
    public void EfFindsTheSameNumberOfMigrations_AsThereAreMigrationClasses()
    {
        using var db = SqlServerContext();

        var classCount = typeof(EmergencyDbContext).Assembly.GetTypes()
            .Count(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract);

        Assert.Equal(classCount, db.Database.GetMigrations().Count());
    }
}
