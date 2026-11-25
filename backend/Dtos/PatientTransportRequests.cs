using MyApplication.Api.Enums;

namespace MyApplication.Api.Dtos;

// ES-FR-014: only incident association, triage, hospital, and optional resident ID.
// The incident ID comes from the URL, not from an editable request body.
public sealed class CreatePatientTransportRequest
{
    public TriageLevel? TriageLevel { get; set; }
    public Guid HospitalId { get; set; }
    public Guid? ResidentId { get; set; }
}

/// <summary>Response DTO; deliberately excludes medical history and diagnoses.</summary>
public sealed record PatientTransportRecordDto(
    Guid Id,
    Guid IncidentId,
    TriageLevel TriageLevel,
    Guid HospitalId,
    Guid? ResidentId,
    DateTime CreatedAt);


