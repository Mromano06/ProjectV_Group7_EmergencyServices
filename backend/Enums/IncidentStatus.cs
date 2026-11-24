namespace MyApplication.Api.Enums;

// Matthew Romano - Nov 14th, 2026 - Incident Status Enum

/// <summary>
/// represents the status of an incident, from reporting to closure
/// req. ES-FR-003
/// </summary>
public enum IncidentStatus 
{ 
    Reported,
    Dispatched, 
    EnRoute,
    OnScene, 
    Resolved,
    Closed
}