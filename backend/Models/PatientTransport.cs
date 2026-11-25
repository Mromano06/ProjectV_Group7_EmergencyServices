using MyApplication.Api.Enums;

namespace MyApplication.Api.Models;

// sebastian solorzano - Nov 21th, 2026 - patient transport model

/// <summary>
/// represents the condition of a patient recovered from an incident
/// req. ES-FR-014
/// </summary>
public class PatientTransport:AuditableEntity
{
    /// <summary> id of incident </summary>
    public Guid IncidentId { get; set; }

    /// <summary> triage level of patient (1=resuscitation, 4=non urgent) </summary>
    public TriageLevel Triagelvl { get; set; }

    /// <summary> id of hospital patient was sent to </summary>
    public Guid HospitalId { get; set; }

    /// <summary> resident id of patient. optional </summary>
    public Guid? ResidentId { get; set; }
}
