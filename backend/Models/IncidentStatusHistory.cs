using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Status History Definitions
// Holds all relevant fields for incident status and history 
public class IncidentStatusHistory : AuditableEntity
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public IncidentStatus FromStatus { get; set; }
    public IncidentStatus ToStatus { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}
