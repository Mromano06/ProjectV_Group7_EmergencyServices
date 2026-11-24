namespace MyApplication.Api.Models;

/// <summary>
/// Base class from the class diagram: every table gets an Id and
/// who/when fields (SH-D04). EmergencyDbContext fills these in automatically.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary> ID of entity </summary>
    public Guid Id { get; set; }

    /// <summary> ID of entity author </summary>
    public Guid CreatedBy { get; set; }

    /// <summary> creation timestamp </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary> ID of entity updater. optional </summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary> timestamp of update. optional </summary>
    public DateTime? UpdatedAt { get; set; }
}
