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

    // Incidents
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentStatusHistory> IncidentStatusHistories => Set<IncidentStatusHistory>();
    public DbSet<IncidentAssignment> IncidentAssignments => Set<IncidentAssignment>();

    // Stations + Units
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<Station> Stations => Set<Station>();

    // Report for incidents/other action
    public DbSet<AfterActionReport> AfterActionReports => Set<AfterActionReport>();

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

        // Station entity creation
        modelBuilder.Entity<Station>(e =>
        {
            e.ToTable("Stations");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.OwnsOne(x => x.Location, loc =>
            {
                loc.Property(p => p.Latitude).HasColumnName("Latitude");
                loc.Property(p => p.Longitude).HasColumnName("Longitude");
                loc.Property(p => p.Address).HasColumnName("Address").HasMaxLength(500);
            });
            e.HasMany(x => x.Units).WithOne(x => x.Station)
                .HasForeignKey(x => x.StationId).OnDelete(DeleteBehavior.Restrict);
        });

        // Unit entity creation
        modelBuilder.Entity<Unit>(e =>
        {
            e.ToTable("Units");
            e.Property(x => x.CallSign).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.CallSign).IsUnique();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.Status, x.Type });
        });

        // Incident entity creation
        modelBuilder.Entity<Incident>(e =>
        {
            e.ToTable("Incidents", t => t.HasCheckConstraint(
                "CK_Incidents_Severity", "[Severity] BETWEEN 1 AND 4"));
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            // Severity deliberately stored as an INT so 1 (Critical) sorts before 4 (Low).
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.ReportedAt).HasConversion(utc);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.CallerName).HasMaxLength(150);
            e.Property(x => x.CallerPhone).HasMaxLength(50);
            e.HasIndex(x => x.SourceCallId).IsUnique().HasFilter("[SourceCallId] IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.Severity, x.ReportedAt });
            e.OwnsOne(x => x.Location, loc =>
            {
                loc.Property(p => p.Latitude).HasColumnName("Latitude");
                loc.Property(p => p.Longitude).HasColumnName("Longitude");
                loc.Property(p => p.Address).HasColumnName("Address").HasMaxLength(500);
            });
            e.HasMany(x => x.Assignments).WithOne(x => x.Incident)
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.StatusHistory).WithOne(x => x.Incident)
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Report).WithOne(x => x.Incident)
                .HasForeignKey<AfterActionReport>(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        });

        // Station entity creation
        modelBuilder.Entity<IncidentAssignment>(e =>
        {
            e.ToTable("IncidentAssignments");
            // Critical ES-NFR-006 safety net: one active assignment per unit.
            e.HasIndex(x => x.UnitId).IsUnique().HasFilter("[ReleasedAt] IS NULL");
            e.HasIndex(x => x.IncidentId);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AssignedAt).HasConversion(utc);
        });

        // Incident Status History entity creation
        modelBuilder.Entity<IncidentStatusHistory>(e =>
        {
            e.ToTable("IncidentStatusHistories");
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ChangedAt).HasConversion(utc);
            e.HasIndex(x => new { x.IncidentId, x.ChangedAt });
        });

        // After Action Report History entity creation
        modelBuilder.Entity<AfterActionReport>(e =>
        {
            e.ToTable("AfterActionReports", t => t.HasCheckConstraint(
                "CK_AAR_Nonnegative", "[Injuries] >= 0 AND [Deaths] >= 0 AND [HydrantWaterLitres] >= 0"));
            e.HasIndex(x => x.IncidentId).IsUnique();
            e.Property(x => x.Outcome).HasMaxLength(1000).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.Property(x => x.FiledAt).HasConversion(utc);
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