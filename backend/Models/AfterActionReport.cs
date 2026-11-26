namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Incident Assignment Definitions
// Holds all relevant fields for incident asssignment 


/// <summary>
/// represents a report detailing the outcomes/conesquences of an incident
/// generated upon resolution of incidents.
///  req. ES-FR-006
/// </summary>
public class AfterActionReport : AuditableEntity
{
    /// <summary> id of the incident </summary>
    public Guid IncidentId { get; set; } 

    /// <summary> reference to incident </summary>
    public Incident Incident { get; set; } = null!;
        //uh guys why do we need this if we already have the guid ?

    /// <summary> conveys the outcome of the incident </summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary> count of injuries sustained </summary>
    public int Injuries { get; set; }

    /// <summary> count of deaths sustained </summary>
    public int Deaths { get; set; }

    /// <summary> count of water hydrant usage in litres </summary>
    public double HydrantWaterLitres { get; set; }
        //uh guys shouldn't we make this a refernce guid to a hydrantusage instead??

    /// <summary> additional report notes </summary>
    public string? Notes { get; set; }

    /// <summary> id of filer </summary>
    public Guid FiledBy { get; set; }

    /// <summary> timestamp </summary>
    public DateTime FiledAt { get; set; }
}
