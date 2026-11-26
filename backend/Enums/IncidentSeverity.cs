namespace MyApplication.Api.Enums;

// Matthew Romano - Nov 14th, 2026 - Incident Enum

/// <summary> 
/// represents the severity of an incident.
/// 1 is Critical; 4 is Low (ES-FR-002).
/// </summary>
public enum IncidentSeverity 
{ 
    Critical = 1,
    High = 2,
    Medium = 3,
    Low = 4
}