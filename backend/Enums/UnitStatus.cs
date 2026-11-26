namespace MyApplication.Api.Enums;

// Matthew Romano - Nov 14th, 2026 - Unit Status Enum

/// <summary>
/// represents the status an emergency response unit is in
/// req. ES-FR-007
/// </summary>
public enum UnitStatus 
{
    Available,
    Dispatched,
    OnScene, 
    OutOfService
}
