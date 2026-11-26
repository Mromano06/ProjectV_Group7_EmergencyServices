using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Status History Definitions
// Holds all relevant fields for incident status and history 

/// <summary>
/// represents changes made to the satus of an incident
/// req. ES-FR-003
/// </summary>
public class IncidentStatusHistory : AuditableEntity
{
    /// <summary> id of incident </summary>
    public Guid IncidentId { get; set; }

    /// <summary> reference to incident </summary>
    public Incident Incident { get; set; } = null!;

    /// <summary> starting / before status of incident </summary>
    public IncidentStatus FromStatus { get; set; }

    /// <summary> ending / after status of incident </summary>
    public IncidentStatus ToStatus { get; set; }

    /// <summary> id of one who changed status </summary>
    public Guid ChangedBy { get; set; }

    /// <summary> timestamp when change was made </summary>
    public DateTime ChangedAt { get; set; }
}
