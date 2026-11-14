namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Assignment Definitions
// Holds all relevant fields for incident asssignment 
public class AfterActionReport : AuditableEntity
{
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public string Outcome { get; set; } = string.Empty;
    public int Injuries { get; set; }
    public int Deaths { get; set; }
    public double HydrantWaterLitres { get; set; }
    public string? Notes { get; set; }
    public Guid FiledBy { get; set; }
    public DateTime FiledAt { get; set; }
}
