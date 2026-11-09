namespace MyApplication.Api.Models;

/// <summary>
/// Base class from the class diagram: every table gets an Id and
/// who/when fields (SH-D04). EmergencyDbContext fills these in automatically.
/// </summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
