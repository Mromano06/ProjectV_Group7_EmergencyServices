using Microsoft.EntityFrameworkCore;
using MyApplication.Api.Data;

namespace MyApplication.Api.Tests.Integration;

/// <summary>
/// The real EmergencyDbContext, with ONE tweak so it runs on SQLite for tests.
///
/// SQL Server fills in [Timestamp] RowVersion columns by itself; SQLite can't. So for tests
/// only, RowVersion is saved as a normal column. Everything else – tables, unique indexes,
/// check constraints, transactions – is exactly the team's real model.
/// </summary>
public class SqliteTestDbContext : EmergencyDbContext
{
    public SqliteTestDbContext(DbContextOptions<EmergencyDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var rowVersion = entityType.FindProperty("RowVersion");
            if (rowVersion is not null && rowVersion.ClrType == typeof(byte[]))
            {
                rowVersion.IsConcurrencyToken = false;
                rowVersion.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }
    }
}
