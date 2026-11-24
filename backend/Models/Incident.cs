using System.ComponentModel.DataAnnotations;
using MyApplication.Api.Enums;
namespace MyApplication.Api.Models;

// Matthew Romano - Nov 14th, 2026 - Main Incident Definition
// Holds all relevant fields for the incident

/// <summary>
/// represents the details of an incident that has occurred.
/// req. ES-FR-002
/// </summary>
public class Incident : AuditableEntity
{
    /// <summary> the type of incident that has occurred (fire, weather, medical, etc.) </summary>
    public IncidentType Type { get; set; }

    /// <summary> severity of the incident (1 critical 4 low) </summary>
    public IncidentSeverity Severity { get; set; }

    /// <summary> status of the incident (reported, dispatched, resolved, etc.) </summary>
    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;

    /// <summary> the location where the incident has occurred </summary>
    public Location Location { get; set; } = new();

    /// <summary> text description of the incident. optional </summary>
    public string? Description { get; set; }

    /// <summary> name of caller that reported incident. optional </summary>
    public string? CallerName { get; set; }

    /// <summary> phone number of caller that reported incident. optional </summary>
    public string? CallerPhone { get; set; }

    /// <summary> id of caller that reported incident. optional </summary>
    public Guid? SourceCallId { get; set; }

    /// <summary> timestamp of incident report time </summary>
    public DateTime ReportedAt { get; set; }

    /// <summary> timestamp of incident close time. optional </summary>
    public DateTime? ClosedAt { get; set; }

    
    [Timestamp] public byte[] RowVersion { get; set; } = [];

    /// <summary> list of units assigned to incident </summary>
    public List<IncidentAssignment> Assignments { get; set; } = [];

    /// <summary> history of incident status </summary>
    public List<IncidentStatusHistory> StatusHistory { get; set; } = [];

    /// <summary> related after action report for incident </summary>
    public AfterActionReport? Report { get; set; }

    /// <summary> related water hydrant usage for incident </summary>
    public HydrantUsage? HydrUsage { get; set; }

    /// <summary> list of patient transports related to the incident </summary>
    public List<PatientTransport> patients { get; set; } = [];
}
