using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyApplication.Api.Models;

namespace MyApplication.Api.Data;

/// <summary>
/// The Emergency Services database (SQL Server via EF Core, SH-D01).
/// </summary>
public class EmergencyDbContext : DbContext
{
    public EmergencyDbContext(DbContextOptions<EmergencyDbContext> options) : base(options)
    {
    }

    //Weather / public warnings
    public DbSet<WeatherWarning> WeatherWarnings => Set<WeatherWarning>();
    public DbSet<WarningIncidentLink> WarningIncidentLinks => Set<WarningIncidentLink>();

    // Hospitals
    public DbSet<Hospital> Hospitals => Set<Hospital>();

    // Add other tables here, e.g.
    // public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SQL Server forgets that a date was UTC when it reads it back.
        // This puts the "UTC" label back on, so the JSON ends in "Z" and the
        // frontend converts it to local time correctly (SH-I04).
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        modelBuilder.Entity<WeatherWarning>(e =>
        {
            e.ToTable("WeatherWarnings");
            e.Property(w => w.StartsAt).HasConversion(utc);
            e.Property(w => w.EndsAt).HasConversion(utc);
            e.Property(w => w.CreatedAt).HasConversion(utc);
            e.Property(w => w.Type).HasMaxLength(100).IsRequired();
            e.Property(w => w.Severity).HasMaxLength(20).IsRequired();
            e.Property(w => w.Area).HasMaxLength(200).IsRequired();
            e.Property(w => w.Description).HasMaxLength(2000);
            e.Property(w => w.Source).HasConversion<string>().HasMaxLength(30); // enums stored as text

            // Speeds up the "active right now" query the banner calls constantly.
            e.HasIndex(w => new { w.StartsAt, w.EndsAt });

            e.HasMany(w => w.IncidentLinks)
             .WithOne(l => l.WeatherWarning)
             .HasForeignKey(l => l.WeatherWarningId)
             .OnDelete(DeleteBehavior.Restrict); // warnings are never hard-deleted
        });

        modelBuilder.Entity<WarningIncidentLink>(e =>
        {
            e.ToTable("WarningIncidentLinks");
            // The same incident can't be linked to the same warning twice.
            e.HasIndex(l => new { l.WeatherWarningId, l.IncidentId }).IsUnique();
            e.HasIndex(l => l.IncidentId); // fast "which warnings is this incident linked to?"
        });

        modelBuilder.Entity<Hospital>(e =>
        {
            e.ToTable("Hospitals");

            // Hospital identification
            e.Property(h => h.Name)
                .HasMaxLength(200)
                .IsRequired();

            e.Property(h => h.Location)
                .HasMaxLength(500)
                .IsRequired();

            // Room capacity
            e.Property(h => h.TotalRooms)
                .IsRequired();

            e.Property(h => h.OccupiedRooms)
                .IsRequired();

            // Store hospital status as text
            e.Property(h => h.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            // Keep timestamps consistent
            e.Property(h => h.CreatedAt)
                .HasConversion(utc);

            // Prevent invalid capacity values
            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_Hospitals_Capacity",
                    "[TotalRooms] >= 0 AND [OccupiedRooms] >= 0 AND [OccupiedRooms] <= [TotalRooms]");
            });
        });

    }

    /// <summary>
    /// Fills in CreatedAt / UpdatedAt automatically on every save (SH-D04),
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}