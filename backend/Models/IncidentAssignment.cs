namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Assignment Definitions
// Holds all relevant fields for incident asssignment 
/// <summary>Never delete assignments; ReleasedAt marks the end of a deployment.</summary>
public class IncidentAssignment : AuditableEntity
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    public DateTime AssignedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
