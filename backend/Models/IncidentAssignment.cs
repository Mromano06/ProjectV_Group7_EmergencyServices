namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Assignment Definitions
// Holds all relevant fields for incident asssignment 

/// <summary>
/// Holds all relevant fields for incident asssignment 
/// Never delete assignments; ReleasedAt marks the end of a deployment.
/// </summary>
public class IncidentAssignment : AuditableEntity
{
    /// <summary> id of the incident </summary>
    public Guid IncidentId { get; set; }

    /// <summary> reference to the incident </summary>
    public Incident Incident { get; set; } = null!;

    /// <summary> id of assigned unit </summary>
    public Guid UnitId { get; set; }

    /// <summary> reference to assigned unit </summary>
    public Unit Unit { get; set; } = null!;

    /// <summary> timestamp for when assignment was made </summary>
    public DateTime AssignedAt { get; set; }

    /// <summary> timestamp for when unit arrived. optional </summary>
    public DateTime? ArrivedAt { get; set; }

    /// <summary> timestamp for whehn unit was released. optional </summary>
    public DateTime? ReleasedAt { get; set; }
}
